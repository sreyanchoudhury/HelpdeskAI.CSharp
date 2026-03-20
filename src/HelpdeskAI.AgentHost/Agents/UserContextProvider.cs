using HelpdeskAI.AgentHost.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Agents;

internal sealed class UserContextProvider : AIContextProvider
{
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        AIContextProvider.InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(UserContext.Email) && string.IsNullOrWhiteSpace(UserContext.Name))
            return ValueTask.FromResult(new AIContext());

        var lines = new List<string> { "## User" };
        if (!string.IsNullOrWhiteSpace(UserContext.Name))
            lines.Add($"Name: {UserContext.Name}");
        if (!string.IsNullOrWhiteSpace(UserContext.Email))
            lines.Add($"Email: {UserContext.Email}");

        return ValueTask.FromResult(new AIContext
        {
            Messages = [new ChatMessage(ChatRole.System, string.Join('\n', lines))]
        });
    }
}
