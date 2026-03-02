using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Normalizes chat message history by merging consecutive assistant tool-call messages
/// into a single message to comply with OpenAI API requirements.
///
/// Transforms:
///   assistant: toolCalls[call_A]
///   assistant: toolCalls[call_B]
///   tool:      result_A
///   tool:      result_B
///
/// Into:
///   assistant: toolCalls[call_A, call_B]
///   tool:      result_A
///   tool:      result_B
/// </summary>
internal sealed class AGUIHistoryNormalizingClient(IChatClient innerClient)
	: DelegatingChatClient(innerClient)
{
	public override Task<ChatResponse> GetResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default) =>
		base.GetResponseAsync(Normalize(messages), options, cancellationToken);

	public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
		IEnumerable<ChatMessage> messages,
		ChatOptions? options = null,
		CancellationToken cancellationToken = default) =>
		base.GetStreamingResponseAsync(Normalize(messages), options, cancellationToken);

	private static IEnumerable<ChatMessage> Normalize(IEnumerable<ChatMessage> messages)
	{
		var list = messages.ToList();
		var result = new List<ChatMessage>(list.Count);
		int i = 0;

		while (i < list.Count)
		{
			var msg = list[i];

			if (!IsToolCallMessage(msg))
			{
				result.Add(msg);
				i++;
				continue;
			}

			// Collect the run of consecutive assistant tool-call messages.
			var group = new List<ChatMessage> { msg };
			i++;
			while (i < list.Count && IsToolCallMessage(list[i]))
			{
				group.Add(list[i]);
				i++;
			}

			if (group.Count == 1)
			{
				result.Add(group[0]);
			}
			else
			{
				// Merge all FunctionCallContent items into one assistant message.
				var allCalls = group
					.SelectMany(m => m.Contents.OfType<FunctionCallContent>())
					.Cast<AIContent>()
					.ToList();
				result.Add(new ChatMessage(ChatRole.Assistant, allCalls));
			}

			// Tool result messages that follow pass through unchanged.
			while (i < list.Count && list[i].Role == ChatRole.Tool)
			{
				result.Add(list[i]);
				i++;
			}
		}

		return result;
	}

	private static bool IsToolCallMessage(ChatMessage m) =>
		m.Role == ChatRole.Assistant &&
		string.IsNullOrWhiteSpace(m.Text) &&
		m.Contents.OfType<FunctionCallContent>().Any();
}