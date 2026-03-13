namespace HelpdeskAI.AgentHost.Models;

//  Configuration models 

public sealed class AzureOpenAiSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ChatDeployment { get; set; } = "gpt-4.1";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";
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


