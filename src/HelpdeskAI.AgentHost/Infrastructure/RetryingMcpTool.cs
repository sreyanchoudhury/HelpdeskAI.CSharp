using System.Diagnostics;
using HelpdeskAI.AgentHost.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Delegates to an MCP AIFunction and automatically reconnects + retries once on any
/// MCP transport or session error (HTTP failures, dropped SSE connections, session expiry).
/// Metadata (Name, Description, schema) always comes from the original inner function.
///
/// Session-staleness fix: instead of caching a mutable _current reference, the live
/// AIFunction is resolved from the provider's cache at each invocation. This means a
/// session refresh by any sibling tool automatically propagates to all wrappers without
/// any explicit coordination — the next invocation naturally picks up the fresh function.
///
/// Emits structured audit logs (toolName, attempt, outcome, durationMs) picked up by
/// Azure Monitor / OpenTelemetry as customDimensions in App Insights traces.
/// </summary>
internal sealed class RetryingMcpTool : DelegatingAIFunction
{
    // The original function from startup — used as a fallback if the provider cache
    // is empty (shouldn't happen after init, but guards against the startup race).
    private readonly AIFunction _initialInner;
    private readonly IMcpToolsProvider _provider;
    private readonly string _toolName;
    private readonly ILogger _logger;

    internal RetryingMcpTool(AIFunction inner, IMcpToolsProvider provider, ILogger logger) : base(inner)
    {
        _initialInner = inner;
        _provider = provider;
        _toolName = inner.Name;
        _logger = logger;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // Resolve the live function from the provider's cache (lock-free read).
        // If any sibling tool triggered a RefreshAsync, _provider now holds the new
        // session's functions and we naturally pick up the correct one here.
        var current = _provider.GetCachedToolOrDefault(_toolName) ?? _initialInner;
        var invocationCount = TurnStateContext.IncrementToolCount(_toolName);

        try
        {
            if (invocationCount > 1)
            {
                _logger.LogWarning(
                    "Tool repeated in the same turn - toolName: {ToolName}, invocationCount: {InvocationCount}.",
                    _toolName, invocationCount);
            }

            var result = await current.InvokeAsync(arguments, cancellationToken);
            _logger.LogInformation(
                "Tool call succeeded — toolName: {ToolName}, attempt: {Attempt}, outcome: {Outcome}, durationMs: {DurationMs}.",
                _toolName, 1, "success", sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
            when (!cancellationToken.IsCancellationRequested && IsMcpTransportError(ex))
        {
            // MCP session expired or transport dropped — reconnect and retry once.
            // RefreshAsync disposes the stale McpClient and creates a fresh session;
            // all sibling RetryingMcpTool wrappers will pick up the new functions on
            // their next invocation via GetCachedToolOrDefault.
            _logger.LogWarning(ex,
                "Tool call transport error — toolName: {ToolName}, attempt: {Attempt}, outcome: {Outcome}, durationMs: {DurationMs}. Reconnecting.",
                _toolName, 1, "transport_error", sw.ElapsedMilliseconds);

            sw.Restart();
            var freshTools = await _provider.RefreshAsync(cancellationToken);
            var replacement = freshTools.FirstOrDefault(t => t.Name == _toolName) ?? _initialInner;

            try
            {
                var result = await replacement.InvokeAsync(arguments, cancellationToken);
                _logger.LogInformation(
                    "Tool call succeeded — toolName: {ToolName}, attempt: {Attempt}, outcome: {Outcome}, durationMs: {DurationMs}.",
                    _toolName, 2, "success_after_retry", sw.ElapsedMilliseconds);
                return result;
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx,
                    "Tool call failed after retry — toolName: {ToolName}, attempt: {Attempt}, outcome: {Outcome}, durationMs: {DurationMs}.",
                    _toolName, 2, "failure", sw.ElapsedMilliseconds);
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
