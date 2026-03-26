using HelpdeskAI.AgentHost.Agents;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Guards <see cref="ThreadIdContext.Current"/> and <see cref="FrontendToolForwardingProvider"/>
/// AsyncLocal values across the MAF workflow boundary.
///
/// <para>
/// Both <see cref="ThreadIdContext"/> and <see cref="FrontendToolForwardingProvider.Capture"/>
/// use <see cref="AsyncLocal{T}"/>, which can be lost when MAF's <c>WorkflowHostAgent</c>
/// spawns tasks with <see cref="System.Threading.ExecutionContext.SuppressFlow"/>. This middleware
/// captures the values on the first pipeline call (from the AG-UI boundary) and restores them on
/// subsequent calls whenever the AsyncLocal values are missing.
/// </para>
///
/// <para>
/// <b>Critical timing:</b> <see cref="AIContextProvider.ProvideAIContextAsync"/> runs BEFORE
/// the chat client pipeline. Since <see cref="AttachmentContextProvider"/> needs ThreadId at
/// context-resolution time, we expose <see cref="EnsureAllContexts"/> which the AG-UI middleware
/// in <c>Program.cs</c> can call to pre-populate AsyncLocals before the agent runs.
/// </para>
/// </summary>
internal sealed class ThreadIdPreservingChatClient : DelegatingChatClient
{
    // Captured once on the first call; restored on subsequent calls if AsyncLocal is lost.
    private string? _capturedThreadId;
    private IReadOnlyList<AITool>? _capturedFrontendTools;
    private readonly ILogger<ThreadIdPreservingChatClient> _logger;

    public ThreadIdPreservingChatClient(IChatClient inner, ILogger<ThreadIdPreservingChatClient> logger)
        : base(inner)
    {
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        EnsureAllContexts(options);
        return await base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureAllContexts(options);
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            yield return update;
    }

    /// <summary>
    /// Restore all AsyncLocal values if lost. Called both from pipeline methods and
    /// from Program.cs middleware to ensure context providers have access.
    /// </summary>
    internal void EnsureAllContexts(ChatOptions? options = null)
    {
        EnsureThreadId();
        EnsureFrontendTools(options);
    }

    private void EnsureThreadId()
    {
        var current = ThreadIdContext.Current;

        if (current is { Length: > 0 })
        {
            _capturedThreadId = current;
            return;
        }

        if (_capturedThreadId is { Length: > 0 })
        {
            ThreadIdContext.Set(_capturedThreadId);
            _logger.LogWarning(
                "[ThreadIdGuard] Restored lost ThreadIdContext: {ThreadId}", _capturedThreadId);
        }
    }

    private void EnsureFrontendTools(ChatOptions? options)
    {
        // Capture frontend tools from the first call (AG-UI boundary has them in ChatOptions.Tools).
        if (options?.Tools is { Count: > 0 } tools)
        {
            var frontendTools = tools
                .Where(t => t.Name.StartsWith("show_", StringComparison.OrdinalIgnoreCase) ||
                            t.Name.StartsWith("suggest_", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (frontendTools.Count > 0)
            {
                _capturedFrontendTools = frontendTools;
                FrontendToolForwardingProvider.Capture(frontendTools);
                _logger.LogDebug("[FrontendToolGuard] Captured {Count} frontend tools", frontendTools.Count);
                return;
            }
        }

        // Restore if AsyncLocal was lost.
        if (_capturedFrontendTools is { Count: > 0 })
        {
            FrontendToolForwardingProvider.Capture(_capturedFrontendTools);
            _logger.LogWarning(
                "[FrontendToolGuard] Restored {Count} lost frontend tools from captured state",
                _capturedFrontendTools.Count);
        }
    }
}
