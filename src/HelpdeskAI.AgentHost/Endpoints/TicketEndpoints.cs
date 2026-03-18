namespace HelpdeskAI.AgentHost.Endpoints;

internal static class TicketEndpoints
{
    internal static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/tickets
        // Proxies to McpServer GET /tickets — keeps McpServer internal-only.
        // Supports: ?requestedBy=, ?status=, ?category=
        app.MapGet("/api/tickets", HandleSearchAsync);
        return app;
    }

    private static async Task<IResult> HandleSearchAsync(
        HttpRequest request,
        IHttpClientFactory httpFactory,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("HelpdeskAI.AgentHost.Endpoints.TicketEndpoints");
        var client = httpFactory.CreateClient("McpServer");

        var qs = request.QueryString.Value ?? string.Empty;
        try
        {
            var response = await client.GetAsync($"tickets{qs}", ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            return Results.Content(content, "application/json", statusCode: (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "McpServer unavailable when proxying /api/tickets");
            return Results.Json(new { error = "McpServer unavailable" }, statusCode: 502);
        }
    }
}
