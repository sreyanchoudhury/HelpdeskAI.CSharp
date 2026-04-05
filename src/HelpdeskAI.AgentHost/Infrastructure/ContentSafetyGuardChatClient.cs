using System.ClientModel;
using System.Runtime.CompilerServices;
using HelpdeskAI.AgentHost.Abstractions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Catches Azure OpenAI <c>content_filter</c> (HTTP 400) exceptions before they propagate
/// unhandled through the IChatClient pipeline and abruptly terminate the AG-UI SSE stream.
///
/// <para>
/// When a content-filter violation is detected the guard:
/// <list type="number">
///   <item>Deletes the thread's Redis history (<c>messages:{thread.id}</c>) so that the
///         poisoned conversation does not keep triggering the filter on every subsequent turn.</item>
///   <item>Throws <see cref="ContentSafetyException"/> with the user-facing ⚠️ message.
///         MAF catches the unhandled exception and emits <c>RUN_ERROR { message }</c>.
///         The frontend <c>CopilotKit.onError</c> handler calls <c>reset()</c> to clear the
///         jailbreak from CopilotKit's local state and sets <c>chatInitial</c> to the ⚠️ text
///         so it appears as the first bubble in the correct chronological position.</item>
///   <item>Logs a warning with the thread.id for App Insights / KQL investigation.</item>
/// </list>
/// </para>
///
/// <para>
/// Insert this immediately after <c>UseFunctionInvocation()</c> in the pipeline so it
/// intercepts both direct LLM calls and tool-call follow-up calls before usage capturing
/// or context-preservation layers see the exception.
/// </para>
/// </summary>
internal sealed class ContentSafetyGuardChatClient(
    IChatClient inner,
    IRedisService redis,
    ILogger<ContentSafetyGuardChatClient> logger) : DelegatingChatClient(inner)
{
    private const string UserFacingMessage =
        "⚠️ Your request was blocked by Azure content safety. " +
        "The conversation history for this thread has been cleared — please send a new message to continue.";

    // ── Non-streaming ──────────────────────────────────────────────────────────

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex) when (IsContentFilterException(ex))
        {
            await HandleContentFilterAsync(ex, cancellationToken);
            throw new ContentSafetyException(UserFacingMessage, ex);
        }
    }

    // ── Streaming ──────────────────────────────────────────────────────────────
    //
    // On a content_filter hit: clear Redis history, log, then throw ContentSafetyException.
    // MAF catches the unhandled iterator exception and emits RUN_ERROR { message }.
    // CopilotKit's onError handler on <CopilotChat> receives it, calls reset() to clear
    // the jailbreak from frontend state, and sets chatInitial to the ⚠️ message.
    //
    // C# does not allow yield inside a catch clause, so we capture the exception in a
    // local variable and throw after the try/catch block.

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var innerStream = base.GetStreamingResponseAsync(messages, options, cancellationToken);
        await using var enumerator = innerStream.GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            ChatResponseUpdate? current = null;
            // C# does not allow yield inside a catch clause, so capture the exception
            // and act on it after the try/catch block.
            Exception? contentSafetyEx = null;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;
                current = enumerator.Current;
            }
            catch (Exception ex) when (IsContentFilterException(ex))
            {
                contentSafetyEx = ex;
            }

            if (contentSafetyEx is not null)
            {
                await HandleContentFilterAsync(contentSafetyEx, cancellationToken);
                // Throw so MAF emits RUN_ERROR { message: UserFacingMessage }.
                // CopilotKit renders RUN_ERROR via the ErrorMessage prop on <CopilotChat>.
                // Yielding a synthetic ChatResponseUpdate instead causes messageId:null
                // Zod validation failures because MAF does not read ResponseId as the AG-UI messageId.
                throw new ContentSafetyException(UserFacingMessage, contentSafetyEx);
            }

            yield return current!;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task HandleContentFilterAsync(Exception ex, CancellationToken ct)
    {
        var tid = ThreadIdContext.Current;

        logger.LogWarning(ex,
            "Azure content_filter blocked request on thread {ThreadId}. Clearing Redis history.",
            tid ?? "(none)");

        if (tid is { Length: > 0 })
        {
            try
            {
                await redis.DeleteAsync($"messages:{tid}");
                logger.LogInformation(
                    "Cleared poisoned history for thread {ThreadId} after content_filter.", tid);
            }
            catch (Exception redisEx)
            {
                // Non-fatal — the graceful message is still returned even if Redis clear fails.
                logger.LogWarning(redisEx,
                    "Failed to clear Redis history for thread {ThreadId}.", tid);
            }
        }
    }

    /// <summary>
    /// Recognises content-filter exceptions from Azure.AI.OpenAI v2 (System.ClientModel)
    /// and from older Azure.Core wrappers, including nested inner exceptions.
    /// </summary>
    internal static bool IsContentFilterException(Exception ex) =>
        ex switch
        {
            // Azure.AI.OpenAI v2 / System.ClientModel path
            ClientResultException cre =>
                cre.Status == 400 &&
                cre.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase),

            // Older Azure.Core / Azure.RequestFailedException path
            Azure.RequestFailedException rfe =>
                string.Equals(rfe.ErrorCode, "content_filter", StringComparison.OrdinalIgnoreCase) ||
                (rfe.Status == 400 &&
                 rfe.Message.Contains("content_filter", StringComparison.OrdinalIgnoreCase)),

            // Unwrap one level of nesting (e.g. AggregateException, InvalidOperationException)
            { InnerException: { } inner } => IsContentFilterException(inner),

            _ => false,
        };
}

/// <summary>
/// Thrown by <see cref="ContentSafetyGuardChatClient"/> when an Azure content_filter
/// (HTTP 400) blocks a request. MAF catches this unhandled exception from the streaming
/// iterator and emits <c>RUN_ERROR { message: ... }</c>. CopilotKit renders the message
/// property as markdown inside a normal assistant bubble.
/// </summary>
internal sealed class ContentSafetyException(string message, Exception inner)
    : Exception(message, inner);
