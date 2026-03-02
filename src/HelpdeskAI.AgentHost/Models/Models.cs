namespace HelpdeskAI.AgentHost.Models;

//  Configuration models 

public sealed class AzureOpenAiSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChatDeployment { get; set; } = "gpt-4.1";
}

public sealed class AzureAiSearchSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string IndexName { get; set; } = "helpdesk-kb";
    public int TopK { get; set; } = 3;
}

public sealed class McpServerSettings
{
    /// <summary>SSE endpoint of HelpdeskAI.McpServer, e.g. http://localhost:5100/mcp</summary>
    public string Endpoint { get; set; } = "http://localhost:5100/mcp";
}

public sealed class ConversationSettings
{
    /// <summary>Summarise when history exceeds this many messages.</summary>
    public int SummarisationThreshold { get; set; } = 10;
    /// <summary>Target message count to reduce history down to after summarisation.</summary>
    public int TailMessagesToKeep { get; set; } = 5;
    public TimeSpan ThreadTtl { get; set; } = TimeSpan.FromDays(1);
}


