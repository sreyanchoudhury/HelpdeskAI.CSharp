using System.Collections.Concurrent;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Thread-safe store for LLM token usage, keyed by AG-UI threadId.
/// UsageCapturingChatClient writes here; RedisChatHistoryProvider reads and removes.
/// A ConcurrentDictionary is used instead of AsyncLocal because the chat client pipeline
/// runs in a child execution context — AsyncLocal changes there do not propagate back
/// to the StoreChatHistoryAsync call in the outer context.
/// </summary>
public static class UsageStore
{
	private static readonly ConcurrentDictionary<string, UsageSnapshot> _store = new();

	public static void Set(string threadId, UsageSnapshot snapshot) =>
		_store[threadId] = snapshot;

	/// <summary>Returns and removes the snapshot for the given threadId, or null if absent.</summary>
	public static UsageSnapshot? TakeAndRemove(string threadId) =>
		_store.TryRemove(threadId, out var s) ? s : null;
}

public sealed record UsageSnapshot(long PromptTokens, long CompletionTokens);
