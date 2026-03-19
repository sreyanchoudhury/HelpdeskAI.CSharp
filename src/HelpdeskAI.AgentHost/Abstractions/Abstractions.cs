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

    /// <summary>
    /// Returns the cached AIFunction for <paramref name="name"/> without acquiring a lock.
    /// Used by RetryingMcpTool to resolve the live function at invocation time, so a
    /// session refresh by any one tool automatically updates all sibling tool wrappers.
    /// Returns null if the cache is empty (startup not complete) or the name isn't found.
    /// </summary>
    AIFunction? GetCachedToolOrDefault(string name);
}

/// <summary>Streams a blob back to the caller with its content type.</summary>
public sealed record BlobDownload(Stream Content, string ContentType);

/// <summary>
/// Uploads a file stream to Blob Storage and returns the blob name (not the unsigned URI).
/// Use the authenticated <c>GET /api/attachments/{blobName}</c> proxy endpoint to serve downloads.
/// </summary>
public interface IBlobStorageService
{
    /// <summary>Uploads the stream and returns the blob name (e.g. <c>{guid}/{fileName}</c>).</summary>
    Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Downloads the blob identified by <paramref name="blobName"/> via Managed Identity.</summary>
    Task<BlobDownload> DownloadAsync(string blobName, CancellationToken ct = default);
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

