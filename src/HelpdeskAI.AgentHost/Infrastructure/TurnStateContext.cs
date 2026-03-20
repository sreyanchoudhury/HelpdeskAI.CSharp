using System.Collections.Concurrent;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal static class TurnStateContext
{
    private static readonly AsyncLocal<string?> _lastUserMessage = new();
    private static readonly AsyncLocal<ConcurrentDictionary<string, int>?> _toolCounts = new();

    public static string? LastUserMessage => _lastUserMessage.Value;

    public static IReadOnlyDictionary<string, int> ToolCounts =>
        _toolCounts.Value ?? new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    internal static void SetLastUserMessage(string? message) =>
        _lastUserMessage.Value = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

    internal static int IncrementToolCount(string toolName)
    {
        var counts = _toolCounts.Value ??= new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return counts.AddOrUpdate(toolName, 1, (_, current) => current + 1);
    }

    internal static void Clear()
    {
        _lastUserMessage.Value = null;
        _toolCounts.Value = null;
    }
}
