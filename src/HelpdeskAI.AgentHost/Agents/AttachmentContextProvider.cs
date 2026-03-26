using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Infrastructure;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

internal sealed class AttachmentContextProvider(
    IAttachmentStore attachmentStore,
    ILogger<AttachmentContextProvider> log,
    bool peek = false) : AIContextProvider
{
    // 5 000 chars ≈ ~1 100 tokens — covers ~2–3 pages; sufficient for most helpdesk attachments.
    private const int MaxExtractedTextLength = 5_000;

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        AIContextProvider.InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        // Use the request-scoped thread ID so attachments are isolated per conversation.
        // No threadId → inject nothing; avoids reading another session's staged attachments.
        var threadId = ThreadIdContext.Current;
        log.LogInformation("[AttachmentCtx] peek={Peek}, ThreadId={ThreadId}",
            peek, threadId ?? "(null)");

        if (threadId is not { Length: > 0 } tid)
            return new AIContext();
        var sessionId = tid;

        IReadOnlyList<ProcessedAttachment> attachments;
        try
        {
            // peek=true: orchestrator reads without clearing so downstream specialists still see it.
            // peek=false (default): consuming read — clears the store after injection.
            attachments = peek
                ? await attachmentStore.LoadAsync(sessionId, cancellationToken)
                : await attachmentStore.LoadAndClearAsync(sessionId, cancellationToken);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to load attachments from store — skipping");
            return new AIContext();
        }

        if (attachments.Count == 0)
            return new AIContext();

        var messages = new List<ChatMessage>();

        foreach (var att in attachments)
        {
            if (att.Kind == AttachmentKind.Text && !string.IsNullOrWhiteSpace(att.ExtractedText))
            {
                var text = att.ExtractedText.Length > MaxExtractedTextLength
                    ? att.ExtractedText[..MaxExtractedTextLength] + "\n\n[Content truncated at 5 000 characters]"
                    : att.ExtractedText;

                var blobArg  = att.BlobUrl is not null ? $", blobUrl=\"{att.BlobUrl}\"" : "";
                var blobLine = att.BlobUrl is not null ? $"Download URL: {att.BlobUrl}\n" : "";
                messages.Add(new ChatMessage(ChatRole.System,
                    $"""
                    ## Attached Document: {att.FileName}
                    {blobLine}
                    {text}

                    [FIRST ACTION REQUIRED]: call show_attachment_preview(fileName="{att.FileName}", summary="<one sentence about the document above>"{blobArg})
                    """));

                log.LogInformation(
                    "Injected text attachment '{FileName}' ({Length} chars) into agent context",
                    att.FileName, text.Length);
            }
            else if (att.Kind == AttachmentKind.Image && !string.IsNullOrWhiteSpace(att.ImageBase64))
            {
                var bytes = Convert.FromBase64String(att.ImageBase64);
                var imageUrlNote = att.BlobUrl is not null ? $" Download URL: {att.BlobUrl}." : "";
                messages.Add(new ChatMessage(ChatRole.User,
                    new List<AIContent>
                    {
                        new TextContent($"The user has attached an image named '{att.FileName}'.{imageUrlNote} Please analyze it."),
                        new DataContent(BinaryData.FromBytes(bytes), att.ContentType)
                    }));

                log.LogInformation(
                    "Injected image '{FileName}' ({Bytes} bytes) as vision content-part",
                    att.FileName, bytes.Length);
            }
        }

        return messages.Count == 0
            ? new AIContext()
            : new AIContext { Messages = messages };
    }
}
