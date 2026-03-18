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
/// Indexes a plain-text document into the knowledge base.
/// Returns the assigned document id so the caller can reference the indexed entry.
/// </summary>
public interface IKnowledgeIngestion
{
    Task<string> IndexDocumentAsync(string title, string content, string? category = null, CancellationToken ct = default);
}

/// <summary>
/// Loads all tools from the MCP server as AIFunction instances.
/// AIFunction is a Microsoft.Extensions.AI type - no SK, no MAF.
/// </summary>
public interface IMcpToolsProvider
{
    Task<IReadOnlyList<AIFunction>> GetToolsAsync(CancellationToken ct = default);

    /// <summary>
    /// Disposes the current MCP session and reconnects, returning fresh AIFunction instances.
    /// Call when tool invocations fail with "Session not found" after a McpServer restart.
    /// </summary>
    Task<IReadOnlyList<AIFunction>> RefreshAsync(CancellationToken ct = default);
}

/// <summary>Uploads a file stream to Blob Storage and returns the blob URL.</summary>
public interface IBlobStorageService
{
    Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken ct = default);
}

/// <summary>
/// Uses Azure Document Intelligence (prebuilt-read) to extract text from
/// PDF, DOCX, PNG, JPG, and JPEG files.
/// </summary>
public interface IDocumentIntelligenceService
{
    Task<string> ExtractTextAsync(string fileName, Stream content, string contentType, CancellationToken ct = default);
}

/// <summary>
/// Session-scoped one-shot staging store for processed attachments.
/// Files are saved after upload and consumed (read + deleted) atomically on the next agent turn.
/// </summary>
public interface IAttachmentStore
{
    Task SaveAsync(string sessionId, IEnumerable<Models.ProcessedAttachment> attachments, CancellationToken ct = default);
    Task<IReadOnlyList<Models.ProcessedAttachment>> LoadAndClearAsync(string sessionId, CancellationToken ct = default);
}

