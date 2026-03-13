using System.ComponentModel;
using HelpdeskAI.McpServer.Services;
using ModelContextProtocol.Server;

namespace HelpdeskAI.McpServer.Tools;

[McpServerToolType]
public static class KnowledgeBaseTools
{
    [McpServerTool(Name = "index_kb_article")]
    [Description("""
        Saves a document or incident resolution to the IT knowledge base so it can be found
        in future searches and used to help other users.
        Call this when the user asks to 'add to KB', 'save to knowledge base', 'index this',
        or when a resolution has been found and should be documented.
        The content you provide will be searchable by future queries.
        """)]
    public static async Task<string> IndexKbArticle(
        KnowledgeBaseService svc,
        [Description("Short, descriptive title for the KB article (max 100 chars)")] string title,
        [Description("Full content of the article — troubleshooting steps, resolution, or reference material")] string content,
        [Description("Category: VPN | Email | Hardware | Network | Access | Printing | Software | Other")] string? category = null)
    {
        try
        {
            var id = await svc.IndexArticleAsync(title, content, category);
            return $"Successfully indexed into the knowledge base. KB article ID: {id}. " +
                   $"It will be available for retrieval in future helpdesk conversations.";
        }
        catch (Exception ex)
        {
            return $"Failed to index article: {ex.Message}";
        }
    }
}
