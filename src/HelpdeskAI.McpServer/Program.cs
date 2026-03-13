using HelpdeskAI.McpServer.Models;
using HelpdeskAI.McpServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KnowledgeBaseSettings>(builder.Configuration.GetSection("AzureAISearch"));
builder.Services.AddSingleton<TicketService>();
builder.Services.AddSingleton<KnowledgeBaseService>();

// ModelContextProtocol.AspNetCore
// WithToolsFromAssembly() discovers all [McpServerToolType] classes
// DI-injected services (TicketService, KnowledgeBaseService) are resolved per-call
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapMcp("/mcp");           // GET /mcp (SSE handshake) + POST /mcp (messages)

// Internal-only REST endpoint — not browser-facing.
// Called exclusively by AgentHost's TicketEndpoints proxy (GET /api/tickets).
app.MapGet("/tickets", (TicketService svc, string? requestedBy, string? status, string? category) =>
{
    TicketStatus?   s = status   is not null && Enum.TryParse<TicketStatus>(status,   true, out var sv) ? sv : null;
    TicketCategory? c = category is not null && Enum.TryParse<TicketCategory>(category, true, out var cv) ? cv : null;
    var tickets = svc.Search(requestedBy, s, c);
    return Results.Ok(tickets.Select(t => new
    {
        t.Id, t.Title, t.Description,
        Status     = t.Status.ToString(),
        Priority   = t.Priority.ToString(),
        Category   = t.Category.ToString(),
        t.RequestedBy, t.AssignedTo, t.CreatedAt, t.UpdatedAt, t.Resolution,
    }));
});

app.MapHealthChecks("/healthz");

await app.RunAsync();

