using HelpdeskAI.McpServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<TicketService>();

// ModelContextProtocol.AspNetCore
// WithToolsFromAssembly() discovers all [McpServerToolType] classes
// DI-injected services (TicketService, KnowledgeBaseService) are resolved per-call
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapMcp("/mcp");           // GET /mcp (SSE handshake) + POST /mcp (messages)
app.MapHealthChecks("/healthz");

await app.RunAsync();

