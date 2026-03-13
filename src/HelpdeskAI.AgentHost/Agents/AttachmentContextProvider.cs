using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

internal sealed class AttachmentContextProvider(
    IAttachmentStore attachmentStore,
    ILogger<AttachmentContextProvider> log) : AIContextProvider
{
    private const string SessionId = "alex.johnson:dev-session";
    private const int MaxExtractedTextLength = 8000;

    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        AIContextProvider.InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProcessedAttachment> attachments;
        try
        {
            attachments = await attachmentStore.LoadAndClearAsync(SessionId, cancellationToken);
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
                    ? att.ExtractedText[..MaxExtractedTextLength] + "\n\n[Content truncated at 8 000 characters]"
                    : att.ExtractedText;

                messages.Add(new ChatMessage(ChatRole.System,
                    $"## Attached Document: {att.FileName}\n\n{text}"));

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
