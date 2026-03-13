using System.Text.Json;
using System.Text.Json.Serialization;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace HelpdeskAI.AgentHost.Infrastructure;

public sealed class RedisChatHistoryProvider : ChatHistoryProvider
{
	private readonly IRedisService _redisService;
	private readonly IChatClient _chatClient;
	private readonly ConversationSettings _settings;
	private readonly ProviderSessionState<ProviderState> _sessionState;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public RedisChatHistoryProvider(
		IRedisService redisService,
		IChatClient chatClient,
		IOptions<ConversationSettings> settings)
	{
		_redisService = redisService;
		_chatClient = chatClient;
		_settings = settings.Value;

		// ThreadIdContext is set by middleware from the AG-UI threadId before this fires.
		_sessionState = new ProviderSessionState<ProviderState>(
			stateInitializer: _ => new ProviderState
			{
				RedisKey = ThreadIdContext.Current is { Length: > 0 } tid
					? $"messages:{tid}"
					: "messages:dev-session"
			},
			stateKey: nameof(RedisChatHistoryProvider),
			jsonSerializerOptions: JsonOptions);
	}

	protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
		InvokingContext context,
		CancellationToken cancellationToken = default)
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

	protected override async ValueTask StoreChatHistoryAsync(
		InvokedContext context,
		CancellationToken cancellationToken = default)
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