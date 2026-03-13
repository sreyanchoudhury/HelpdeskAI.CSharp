namespace HelpdeskAI.AgentHost.Infrastructure;

// Holds the AG-UI threadId for the current async call chain.
// Set by middleware in Program.cs before the request reaches any handler.
internal static class ThreadIdContext
{
	private static readonly AsyncLocal<string?> _current = new();

	public static string? Current => _current.Value;

	internal static void Set(string? threadId) => _current.Value = threadId;
}
