using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Injects <c>stream_options: { include_usage: true }</c> into every streaming
/// Azure OpenAI chat completion request so that token counts are returned in the
/// final SSE chunk and <see cref="UsageCapturingChatClient"/> can persist them to Redis.
///
/// <para>
/// Azure OpenAI omits token usage from streaming responses unless this option is
/// explicitly requested. MEAI's <c>OpenAIChatClient</c> does not set it automatically,
/// so we inject it via a per-call pipeline policy on the underlying <c>AzureOpenAIClient</c>.
/// </para>
/// </summary>
internal sealed class IncludeStreamingUsagePolicy : PipelinePolicy
{
    public static readonly IncludeStreamingUsagePolicy Instance = new();

    public override void Process(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        TryInjectStreamingUsage(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override async ValueTask ProcessAsync(
        PipelineMessage message,
        IReadOnlyList<PipelinePolicy> pipeline,
        int currentIndex)
    {
        TryInjectStreamingUsage(message);
        await ProcessNextAsync(message, pipeline, currentIndex);
    }

    private static void TryInjectStreamingUsage(PipelineMessage message)
    {
        var content = message.Request.Content;
        if (content is null) return;

        // Read the outbound request body.
        using var buffer = new MemoryStream();
        content.WriteTo(buffer, CancellationToken.None);
        buffer.Position = 0;

        using var doc = JsonDocument.Parse(buffer);
        var root = doc.RootElement;

        // Only act on requests that have "stream": true.
        if (!root.TryGetProperty("stream", out var streamProp) ||
            streamProp.ValueKind != JsonValueKind.True)
            return;

        // Don't overwrite if the caller already set stream_options.
        if (root.TryGetProperty("stream_options", out _)) return;

        // Rebuild the JSON body with stream_options appended.
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
                prop.WriteTo(writer);
            writer.WritePropertyName("stream_options");
            writer.WriteStartObject();
            writer.WriteBoolean("include_usage", true);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        message.Request.Content = BinaryContent.Create(BinaryData.FromBytes(output.ToArray()));
    }
}
