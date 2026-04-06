namespace HelpdeskAI.AgentHost.Models;

//  Configuration models 

public sealed class AzureOpenAiSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChatDeployment { get; set; } = "gpt-5.3-chat";
    /// <summary>Optional separate deployment for the v2 multi-agent workflow. Falls back to <see cref="ChatDeployment"/> if empty.</summary>
    public string ChatDeploymentV2 { get; set; } = "gpt-5.2-chat";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
}

public sealed class EvaluationSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string ScorerEndpoint { get; set; } = string.Empty;
    public string ScorerApiKey { get; set; } = string.Empty;
    public string ScorerDeployment { get; set; } = string.Empty;
}

public sealed class DynamicToolsSettings
{
    public int TopK { get; set; } = 5;
}

public sealed class AzureAiSearchSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string IndexName { get; set; } = "helpdesk-kb";
    public int TopK { get; set; } = 3;
    /// <summary>
    /// Minimum semantic reranker score (0–4) for a KB result to be injected into context.
    /// Results below this threshold are silently discarded, reducing irrelevant token usage.
    /// Set to 0.0 to disable filtering.
    /// </summary>
    public double MinScore { get; set; } = 1.5;
}

public sealed class McpServerSettings
{
    /// <summary>SSE endpoint of HelpdeskAI.McpServer, e.g. http://localhost:5100/mcp</summary>
    public string Endpoint { get; set; } = "http://localhost:5100/mcp";
}

public sealed class ConversationSettings
{
    /// <summary>Summarise when history exceeds this many messages.</summary>
    public int SummarisationThreshold { get; set; } = 8;
    /// <summary>Keep this many recent raw messages after summarisation.</summary>
    public int TailMessagesToKeep { get; set; } = 3;
    public TimeSpan ThreadTtl { get; set; } = TimeSpan.FromDays(1);
}

public sealed class AzureBlobStorageSettings
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "helpdesk-attachments";
}

public sealed class DocumentIntelligenceSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}

public sealed class EntraAuthSettings
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
}

public sealed class LongTermMemorySettings
{
    public TimeSpan ProfileTtl { get; set; } = TimeSpan.FromDays(90);
}

// Attachment models

public enum AttachmentKind { Text, Image }

public sealed class ProcessedAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "text/plain";
    public AttachmentKind Kind { get; set; } = AttachmentKind.Text;
    /// <summary>Extracted text for .txt / .pdf / .docx files. Null for images.</summary>
    public string? ExtractedText { get; set; }
    /// <summary>Base64-encoded bytes for image files, used for vision content-part injection. Null for non-image files.</summary>
    public string? ImageBase64 { get; set; }
    /// <summary>Permanent Blob Storage URL for the archived file.</summary>
    public string? BlobUrl { get; set; }
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}

// Knowledge base search result (returned by AzureAiSearchService.SearchStructuredAsync)

internal sealed record KbSearchResult(string Id, string Title, string Content, string? Category);


