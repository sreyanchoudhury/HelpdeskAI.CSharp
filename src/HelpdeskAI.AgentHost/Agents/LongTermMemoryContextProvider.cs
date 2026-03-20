using HelpdeskAI.AgentHost.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

internal sealed class LongTermMemoryContextProvider(
    LongTermMemoryStore store,
    ILogger<LongTermMemoryContextProvider> logger) : AIContextProvider
{
    protected override async ValueTask<AIContext> ProvideAIContextAsync(
        AIContextProvider.InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        if (UserContext.Email is not { Length: > 0 } email)
            return new AIContext();

        try
        {
            var profile = await store.GetProfileAsync(email, cancellationToken);
            if (profile is null)
                return new AIContext();

            var lines = new List<string>
            {
                "## User Memory",
                $"Known Email: {profile.Email}",
                $"Last Seen: {profile.LastSeenAt:O}"
            };

            if (!string.IsNullOrWhiteSpace(profile.Name))
                lines.Insert(1, $"Known Name: {profile.Name}");
            if (profile.Preferences.Length > 0)
                lines.Add($"Preferences: {string.Join("; ", profile.Preferences)}");

            return new AIContext
            {
                Messages = [new ChatMessage(ChatRole.System, string.Join('\n', lines))]
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load long-term memory for {Email}", email);
            return new AIContext();
        }
    }
}
