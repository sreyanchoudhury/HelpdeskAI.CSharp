using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using HelpdeskAI.McpServer.Infrastructure;
using HelpdeskAI.McpServer.Models;
using HelpdeskAI.McpServer.Services;
using Microsoft.Azure.Cosmos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<KnowledgeBaseSettings>(builder.Configuration.GetSection("AzureAISearch"));
builder.Services.Configure<CosmosDbSettings>(builder.Configuration.GetSection("CosmosDb"));

// CosmosClient: singleton, configured with System.Text.Json + camelCase + string enums
// so document field names (id, status, createdAt…) match Patch paths
builder.Services.AddSingleton(sp =>
{
    var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CosmosDbSettings>>().Value;
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters           = { new JsonStringEnumConverter() },
    };
    return new CosmosClient(settings.Endpoint, settings.PrimaryKey, new CosmosClientOptions
    {
        Serializer = new CosmosStjSerializer(jsonOptions),
    });
});

// Container (direct child of CosmosClient) — resolved from settings
builder.Services.AddSingleton(sp =>
{
    var cosmosClient = sp.GetRequiredService<CosmosClient>();
    var settings     = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CosmosDbSettings>>().Value;
    return cosmosClient.GetDatabase(settings.DatabaseName).GetContainer(settings.ContainerName);
});

builder.Services.AddSingleton<TicketService>();
builder.Services.AddSingleton<KnowledgeBaseService>();
builder.Services.AddSingleton<SystemStatusService>();

// ModelContextProtocol.AspNetCore
// WithToolsFromAssembly() discovers all [McpServerToolType] classes
// DI-injected services (TicketService, KnowledgeBaseService) are resolved per-call
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

builder.Services.AddHealthChecks();
builder.Services.AddOpenTelemetry().UseAzureMonitor();

var app = builder.Build();

// Initialise TicketService — seeds Cosmos if empty, restores ID counter if data exists
await app.Services.GetRequiredService<TicketService>().InitializeAsync();

app.MapMcp("/mcp");           // GET /mcp (SSE handshake) + POST /mcp (messages)

// Internal-only REST endpoint — not browser-facing.
// Called exclusively by AgentHost's TicketEndpoints proxy (GET /api/tickets).
app.MapGet("/tickets", async (TicketService svc, string? requestedBy, string? status, string? category) =>
{
    TicketStatus? s = status is not null && Enum.TryParse<TicketStatus>(status, true, out var sv) ? sv : null;
    TicketCategory? c = category is not null && Enum.TryParse<TicketCategory>(category, true, out var cv) ? cv : null;
    var tickets = await svc.SearchAsync(requestedBy, s, c);
    return Results.Ok(tickets.Select(t => new
    {
        t.Id,
        t.Title,
        t.Description,
        Status   = t.Status.ToString(),
        Priority = t.Priority.ToString(),
        Category = t.Category.ToString(),
        t.RequestedBy,
        t.AssignedTo,
        t.CreatedAt,
        t.UpdatedAt,
        t.Resolution,
        t.UserSentiment,
        t.EscalationReason,
        t.ImpactScope,
        t.RelatedIncidentIds,
    }));
});

app.MapGet("/incidents/active", (SystemStatusService svc) =>
{
    var incidents = svc.GetActiveIncidents()
        .Select(s => new
        {
            service = s.Name,
            severity = s.Health.ToString().ToLowerInvariant(),
            incidentId = s.IncidentId,
            message = s.StatusMessage,
            workaround = s.Workaround,
            eta = s.EstimatedResolve.HasValue ? s.EstimatedResolve.Value.ToString("t") + " UTC" : null,
        })
        .ToList();

    return Results.Ok(new
    {
        count = incidents.Count,
        incidents,
        checkedAt = DateTimeOffset.UtcNow,
    });
});

app.MapHealthChecks("/healthz");

await app.RunAsync();
