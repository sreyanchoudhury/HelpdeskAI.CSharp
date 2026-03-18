using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Infrastructure;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

internal sealed class AttachmentContextProvider(
    IAttachmentStore attachmentStore,
    ILogger<AttachmentContextProvider> log) : AIContextProvider
{
    // 5 000 chars ≈ ~1 100 tokens — covers ~2–3 pages; sufficient for most helpdesk attachments.
    private const int MaxExtractedTextLength = 5_000;

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        AIContextProvider.InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        // Use the request-scoped thread ID so attachments are isolated per conversation.
        // Falls back to "dev-session" when running locally without a thread ID header.
        var sessionId = ThreadIdContext.Current is { Length: > 0 } tid ? tid : "dev-session";

        IReadOnlyList<ProcessedAttachment> attachments;
        try
        {
            attachments = await attachmentStore.LoadAndClearAsync(sessionId, cancellationToken);
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

                messages.Add(new ChatMessage(ChatRole.System,
                    $"## Attached Document: {att.FileName}\n\n{text}"));

                // User-role nudge mirrors image attachment behaviour — ensures the model
                // reads the attachment before reaching for ticket/incident lookup tools.
                messages.Add(new ChatMessage(ChatRole.User,
                    $"I've attached '{att.FileName}' above. Please use it to answer my next message."));

                log.LogInformation(
                    "Injected text attachment '{FileName}' ({Length} chars) into agent context",
                    att.FileName, text.Length);
            }
            else if (att.Kind == AttachmentKind.Image && !string.IsNullOrWhiteSpace(att.ImageBase64))
            {
                var bytes = Convert.FromBase64String(att.ImageBase64);
                messages.Add(new ChatMessage(ChatRole.User,
                    new List<AIContent>
                    {
                        new TextContent($"The user has attached an image named '{att.FileName}'. Please analyze it."),
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
