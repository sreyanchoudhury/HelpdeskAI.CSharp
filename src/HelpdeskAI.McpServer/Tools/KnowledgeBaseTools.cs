using System.ComponentModel;
using System.Text.Json;
using HelpdeskAI.McpServer.Services;
using ModelContextProtocol.Server;

namespace HelpdeskAI.McpServer.Tools;

[McpServerToolType]
public static class KnowledgeBaseTools
{
    [McpServerTool(Name = "search_kb_articles")]
    [Description("""
        Searches the IT knowledge base and returns matching articles.
        Use this whenever the user asks to see, find, browse, or show KB articles.
        Returns a single article card when one result is found, or a list when multiple are found.
        """)]
    public static async Task<string> SearchKbArticles(
        KnowledgeBaseService svc,
        [Description("Search query e.g. 'VPN setup Windows', 'Outlook not opening'")] string query,
        [Description("Category filter: VPN | Email | Hardware | Network | Access | Printing | Software | Other (optional)")] string? category = null)
    {
        try
        {
            var articles = await svc.SearchAsync(query, category, topK: 5);
            if (articles.Count == 0)
                return JsonSerializer.Serialize(new { count = 0, message = "No KB articles found for that query." });

            if (articles.Count == 1)
            {
                var a = articles[0];
                return JsonSerializer.Serialize(new
                {
                    count         = 1,
                    articles      = articles.Select(x => new { x.Id, x.Title, x.Category }),
                    _renderAction = "show_kb_article",
                    _renderArgs   = new { id = a.Id, title = a.Title, content = a.Content, category = a.Category },
                });
            }

            var summaries = articles.Select(a => new
            {
                id       = a.Id,
                title    = a.Title,
                category = a.Category,
                summary  = a.Content.Length > 140 ? a.Content[..140].TrimEnd() + "…" : a.Content,
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                count         = articles.Count,
                _renderAction = "suggest_related_articles",
                _renderArgs   = new { articles = JsonSerializer.Serialize(summaries) },
            });
        }
        catch (Exception ex)
        {
            return $"Failed to search knowledge base: {ex.Message}";
        }
    }

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
            return JsonSerializer.Serialize(new
            {
                id,
                title,
                category,
                message       = $"Successfully indexed. KB article ID: {id}",
                _renderAction = "show_kb_article",
                _renderArgs   = new { id, title, content, category },
            });
        }
        catch (Exception ex)
        {
            return $"Failed to index article: {ex.Message}";
        }
    }
}
