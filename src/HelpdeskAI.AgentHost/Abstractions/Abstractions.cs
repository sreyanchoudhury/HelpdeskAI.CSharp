using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Abstractions;

/// <summary>Thin Redis wrapper - only class that touches StackExchange.Redis types.</summary>
public interface IRedisService
{
	Task<string?> GetAsync(string key);
	Task SetAsync(string key, string value, TimeSpan ttl);
	Task DeleteAsync(string key);
}

/// <summary>RAG retrieval - returns a formatted context block or null if no results.</summary>
public interface IKnowledgeSearch
{
    Task<string?> SearchAsync(string query, CancellationToken ct = default);
}

/// <summary>
/// Loads all tools from the MCP server as AIFunction instances.
/// AIFunction is a Microsoft.Extensions.AI type - no SK, no MAF.
/// </summary>
public interface IMcpToolsProvider
{
    Task<IReadOnlyList<AIFunction>> GetToolsAsync(CancellationToken ct = default);
}

