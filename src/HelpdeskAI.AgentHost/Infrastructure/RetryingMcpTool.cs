using System.Diagnostics;
using HelpdeskAI.AgentHost.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Delegates to an MCP AIFunction and automatically reconnects + retries once on any
/// MCP transport or session error (HTTP failures, dropped SSE connections, session expiry).
/// Metadata (Name, Description, schema) always comes from the original inner function.
/// Emits structured audit logs (toolName, attempt, outcome, durationMs) picked up by
/// Azure Monitor / OpenTelemetry as customDimensions in App Insights traces.
/// </summary>
internal sealed class RetryingMcpTool : DelegatingAIFunction
{
    // Tracks the latest live function; replaced after a successful reconnect.
    private AIFunction _current;
    private readonly IMcpToolsProvider _provider;
    private readonly string _toolName;
    private readonly ILogger _logger;

    internal RetryingMcpTool(AIFunction inner, IMcpToolsProvider provider, ILogger logger) : base(inner)
    {
        _current = inner;
        _provider = provider;
        _toolName = inner.Name;
        _logger = logger;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        // BeginScope properties surface as customDimensions.toolName in App Insights.
        using var scope = _logger.BeginScope(new { toolName = _toolName });

        try
        {
            var result = await _current.InvokeAsync(arguments, cancellationToken);
            _logger.LogInformation(
                "Tool call succeeded — attempt: {Attempt}, outcome: {Outcome}, durationMs: {DurationMs}.",
                1, "success", sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
            when (!cancellationToken.IsCancellationRequested && IsMcpTransportError(ex))
        {
            // MCP session expired or transport dropped — reconnect and retry once.
            // The McpToolsProvider.RefreshAsync disposes the stale client and creates a
            // fresh MCP session; GetToolsAsync also proactively refreshes at 3 min.
            _logger.LogWarning(ex,
                "Tool call transport error — attempt: {Attempt}, outcome: {Outcome}, durationMs: {DurationMs}. Reconnecting.",
                1, "transport_error", sw.ElapsedMilliseconds);

            sw.Restart();
            var freshTools = await _provider.RefreshAsync(cancellationToken);
            var replacement = freshTools.FirstOrDefault(t => t.Name == _toolName);
            if (replacement is not null) _current = replacement;

            try
            {
                var result = await _current.InvokeAsync(arguments, cancellationToken);
                _logger.LogInformation(
                    "Tool call succeeded — attempt: {Attempt}, outcome: {Outcome}, durationMs: {DurationMs}.",
                    2, "success_after_retry", sw.ElapsedMilliseconds);
                return result;
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx,
                    "Tool call failed after retry — attempt: {Attempt}, outcome: {Outcome}, durationMs: {DurationMs}.",
                    2, "failure", sw.ElapsedMilliseconds);
                throw;
            }
        }
    }

    /// <summary>
    /// Returns true for errors that indicate the MCP transport or session has broken down
    /// and a reconnect is likely to succeed. Does NOT match MCP-level errors like invalid
    /// arguments, which would fail again after reconnection.
    /// </summary>
    private static bool IsMcpTransportError(Exception ex)
    {
        // Any HTTP transport failure (connection refused, reset, 400/404 from expired session).
        if (ex is HttpRequestException) return true;

        // Internal HttpClient timeout: the GET SSE channel was cut (e.g. Azure 240s ingress
        // limit) while a tool call was in-flight; the outer CT is still live, so this is a
        // transport failure, not a user-initiated cancellation.
        if (ex is TaskCanceledException or OperationCanceledException) return true;

        // Transport in invalid state (e.g. disposed SSE stream).
        if (ex is InvalidOperationException ioe &&
            (ioe.Message.Contains("transport", StringComparison.OrdinalIgnoreCase) ||
             ioe.Message.Contains("session", StringComparison.OrdinalIgnoreCase) ||
             ioe.Message.Contains("disconnect", StringComparison.OrdinalIgnoreCase) ||
             ioe.Message.Contains("closed", StringComparison.OrdinalIgnoreCase)))
            return true;

        // Explicit "Session not found" / "Session ID" messages from MCP SDK.
        if (ex.Message.Contains("Session not found", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("Session ID", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
