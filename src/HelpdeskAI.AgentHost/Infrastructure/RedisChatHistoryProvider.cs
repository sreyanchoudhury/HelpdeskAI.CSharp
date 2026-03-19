using System.Text.Json;
using System.Text.Json.Serialization;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HelpdeskAI.AgentHost.Infrastructure;

public sealed class RedisChatHistoryProvider : ChatHistoryProvider
{
    private readonly IRedisService _redisService;
    private readonly IChatClient _chatClient;
    private readonly ConversationSettings _settings;
    private readonly ProviderSessionState<ProviderState> _sessionState;
    private readonly ILogger<RedisChatHistoryProvider> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public RedisChatHistoryProvider(
        IRedisService redisService,
        IChatClient chatClient,
        IOptions<ConversationSettings> settings,
        ILogger<RedisChatHistoryProvider> logger)
    {
        _redisService = redisService;
        _chatClient = chatClient;
        _settings = settings.Value;
        _logger = logger;

        // ThreadIdContext is set by middleware from the AG-UI threadId before this fires.
        _sessionState = new ProviderSessionState<ProviderState>(
            stateInitializer: _ => new ProviderState
            {
                // No threadId → ephemeral per-request key; starts fresh, never crosses session boundaries.
                RedisKey = ThreadIdContext.Current is { Length: > 0 } tid
                    ? $"messages:{tid}"
                    : $"messages:anon:{Guid.NewGuid():N}"
            },
            stateKey: nameof(RedisChatHistoryProvider),
            jsonSerializerOptions: JsonOptions);
    }

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var state = _sessionState.GetOrInitializeState(context.Session);
            var json = await _redisService.GetAsync(state.RedisKey);

            if (string.IsNullOrEmpty(json))
                return [];

            var messages = JsonSerializer.Deserialize<List<SerializableChatMessage>>(json, JsonOptions) ?? [];

            var reducer = new SummarizingChatReducer(
                _chatClient,
                _settings.TailMessagesToKeep,
                _settings.SummarisationThreshold);

            return await reducer.ReduceAsync(messages.Select(m => m.ToChatMessage()), cancellationToken);
        }
        catch (Exception ex)
        {
            // Redis unavailable (e.g. connection not yet established, transient network issue).
            // Degrade gracefully: start the turn with empty history rather than crashing the agent.
            _logger.LogWarning(ex, "Redis read failed; starting turn with empty history.");
            return [];
        }
    }

    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var state = _sessionState.GetOrInitializeState(context.Session);

            var existing = new List<SerializableChatMessage>();
            var json = await _redisService.GetAsync(state.RedisKey);
            if (!string.IsNullOrEmpty(json))
                existing = JsonSerializer.Deserialize<List<SerializableChatMessage>>(json, JsonOptions) ?? [];

            var newMessages = context.RequestMessages
                .Concat(context.ResponseMessages ?? [])
                .Where(m =>
                    (m.Role == ChatRole.User || m.Role == ChatRole.Assistant) &&
                    !string.IsNullOrWhiteSpace(m.Text))
                .Select(SerializableChatMessage.FromChatMessage);

            existing.AddRange(newMessages);

            await _redisService.SetAsync(
                state.RedisKey,
                JsonSerializer.Serialize(existing, JsonOptions),
                _settings.ThreadTtl);

            _sessionState.SaveState(context.Session, state);
        }
        catch (Exception ex)
        {
            // Redis unavailable — history won't be persisted for this turn, but the
            // response has already been streamed. Log and continue.
            _logger.LogWarning(ex, "Redis write failed; history not persisted.");
        }
    }
}

public sealed class ProviderState
{
    [JsonPropertyName("redisKey")]
    public string RedisKey { get; set; } = string.Empty;
}

public sealed class SerializableChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    public static SerializableChatMessage FromChatMessage(ChatMessage m) => new()
    {
        Role = m.Role.Value,
        Content = m.Text ?? string.Empty
    };

    public ChatMessage ToChatMessage() => new(new ChatRole(Role), Content);
}