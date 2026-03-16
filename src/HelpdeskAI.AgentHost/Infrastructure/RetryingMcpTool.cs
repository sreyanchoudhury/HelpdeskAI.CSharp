using HelpdeskAI.AgentHost.Abstractions;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Delegates to an MCP AIFunction and automatically reconnects + retries once when a
/// "Session not found" error is returned after a McpServer restart.
/// Metadata (Name, Description, schema) always comes from the original inner function.
/// </summary>
internal sealed class RetryingMcpTool : DelegatingAIFunction
{
    // Tracks the latest live function; replaced after a successful reconnect.
    private AIFunction _current;
    private readonly IMcpToolsProvider _provider;
    private readonly string _toolName;

    internal RetryingMcpTool(AIFunction inner, IMcpToolsProvider provider) : base(inner)
    {
        _current  = inner;
        _provider = provider;
        _toolName = inner.Name;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        try
        {
            return await _current.InvokeAsync(arguments, cancellationToken);
        }
        catch (HttpRequestException ex)
            when (!cancellationToken.IsCancellationRequested &&
                  ex.Message.Contains("Session not found", StringComparison.OrdinalIgnoreCase))
        {
            // MCP session expired after McpServer restart — reconnect and retry once.
            var freshTools = await _provider.RefreshAsync(cancellationToken);
            var replacement = freshTools.FirstOrDefault(t => t.Name == _toolName);
            if (replacement is not null) _current = replacement;
            return await _current.InvokeAsync(arguments, cancellationToken);
        }
    }
}
