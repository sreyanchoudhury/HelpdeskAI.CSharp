namespace HelpdeskAI.AgentHost.Infrastructure;

internal static class UserContext
{
    private static readonly AsyncLocal<string?> _name = new();
    private static readonly AsyncLocal<string?> _email = new();

    public static string? Name => _name.Value;
    public static string? Email => _email.Value;

    internal static void Set(string? name, string? email)
    {
        _name.Value = string.IsNullOrWhiteSpace(name) ? null : name;
        _email.Value = string.IsNullOrWhiteSpace(email) ? null : email;
    }

    internal static void Clear() => Set(null, null);
}
