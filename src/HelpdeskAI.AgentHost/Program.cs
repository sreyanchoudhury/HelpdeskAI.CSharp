using Azure.AI.OpenAI;
using Azure.Identity;
using System.ClientModel.Primitives;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Agents;
using HelpdeskAI.AgentHost.Endpoints;
using HelpdeskAI.AgentHost.Infrastructure;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;


builder.Services.Configure<AzureOpenAiSettings>(cfg.GetSection("AzureOpenAI"));
builder.Services.Configure<AzureAiSearchSettings>(cfg.GetSection("AzureAISearch"));
builder.Services.Configure<McpServerSettings>(cfg.GetSection("McpServer"));
builder.Services.Configure<ConversationSettings>(cfg.GetSection("Conversation"));
builder.Services.Configure<AzureBlobStorageSettings>(cfg.GetSection("AzureBlobStorage"));
builder.Services.Configure<DocumentIntelligenceSettings>(cfg.GetSection("DocumentIntelligence"));

var aiSettings = cfg.GetSection("AzureOpenAI").Get<AzureOpenAiSettings>()
    ?? throw new InvalidOperationException("AzureOpenAI config section missing");

// Attach the streaming-usage policy so Azure returns token counts in the final SSE
// chunk — MEAI's OpenAIChatClient does not request this flag automatically.
var chatClientOptions = new AzureOpenAIClientOptions();
chatClientOptions.AddPolicy(IncludeStreamingUsagePolicy.Instance, PipelinePosition.PerCall);

IChatClient azureClient = string.IsNullOrWhiteSpace(aiSettings.ApiKey)
    ? new AzureOpenAIClient(new Uri(aiSettings.Endpoint), new DefaultAzureCredential(), chatClientOptions)
          .GetChatClient(aiSettings.ChatDeployment)
          .AsIChatClient()
    : new AzureOpenAIClient(
              new Uri(aiSettings.Endpoint),
              new Azure.AzureKeyCredential(aiSettings.ApiKey), chatClientOptions)
          .GetChatClient(aiSettings.ChatDeployment)
          .AsIChatClient();

IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator =
    string.IsNullOrWhiteSpace(aiSettings.ApiKey)
        ? new AzureOpenAIClient(new Uri(aiSettings.Endpoint), new DefaultAzureCredential())
              .GetEmbeddingClient(aiSettings.EmbeddingDeployment)
              .AsIEmbeddingGenerator()
        : new AzureOpenAIClient(
                  new Uri(aiSettings.Endpoint),
                  new Azure.AzureKeyCredential(aiSettings.ApiKey))
              .GetEmbeddingClient(aiSettings.EmbeddingDeployment)
              .AsIEmbeddingGenerator();

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisCs = cfg.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Redis connection string missing");
    // abortConnect=false: non-blocking — app starts even if Redis isn't reachable yet.
    // syncTimeout: wait up to 15 s for a command to be sent — gives the multiplexer
    //   time to establish the connection after the Container App's DNS becomes ready.
    // keepAlive: ping every 60 s to prevent the Azure TCP proxy from dropping idle connections.
    var options = ConfigurationOptions.Parse(redisCs);
    options.AbortOnConnectFail = false;
    options.ConnectTimeout = 10000;
    options.SyncTimeout = 15000;
    options.KeepAlive = 60;
    return ConnectionMultiplexer.Connect(options);
});

builder.Services.AddSingleton<IRedisService, RedisService>();
builder.Services.AddSingleton<AzureAiSearchService>();
builder.Services.AddSingleton<IKnowledgeSearch>(sp => sp.GetRequiredService<AzureAiSearchService>());
builder.Services.AddSingleton<IKnowledgeIngestion>(sp => sp.GetRequiredService<AzureAiSearchService>());
builder.Services.AddSingleton<IMcpToolsProvider, McpToolsProvider>();
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();

// Named HttpClient for internal McpServer calls (base URL derived from MCP endpoint).
// Standard resilience pipeline: 3 retries with exponential backoff + 30 s total timeout.
var mcpBase = new Uri(cfg["McpServer:Endpoint"] ?? "http://localhost:5100/mcp");
builder.Services.AddHttpClient("McpServer", c =>
    c.BaseAddress = new Uri($"{mcpBase.Scheme}://{mcpBase.Authority}/"))
    .AddStandardResilienceHandler(o =>
    {
        o.Retry.MaxRetryAttempts = 3;
        o.Retry.Delay = TimeSpan.FromMilliseconds(300);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    });
builder.Services.AddSingleton<IAttachmentStore, RedisAttachmentStore>();
builder.Services.AddSingleton<IDocumentIntelligenceService, DocumentIntelligenceService>();

builder.Services.AddChatClient(azureClient)
    .UseFunctionInvocation()
    .Use((inner, services) => new UsageCapturingChatClient(
        inner,
        services.GetRequiredService<IRedisService>(),
        services.GetRequiredService<IOptions<ConversationSettings>>()))
    .Use((inner, _) => new AGUIHistoryNormalizingClient(inner))
    .UseLogging()
    .UseOpenTelemetry();

var allowedOrigins = cfg["AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries)
    ?? ["http://localhost:3000"];

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
{
    if (allowedOrigins is ["*"])
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else
        p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddMemoryCache();
builder.Services.AddOpenTelemetry().UseAzureMonitor();

// Redis is ephemeral/non-blocking — exclude it from the liveness check so
// /healthz returns 200 whenever the app is running, regardless of Redis state.
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseCors();

// Populate ThreadIdContext before the request is handled so history providers
// can key Redis by AG-UI threadId without access to the (always-null) AgentSession.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/agent") &&
        context.Request.Method == "POST")
    {
        context.Request.EnableBuffering();
        using var reader = new System.IO.StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("threadId", out var elem))
            ThreadIdContext.Set(elem.GetString());
    }
    await next(context);
});

var chatClient = app.Services.GetRequiredService<IChatClient>();

var historyProvider = new RedisChatHistoryProvider(
    app.Services.GetRequiredService<IRedisService>(),
    chatClient,
    app.Services.GetRequiredService<IOptions<ConversationSettings>>(),
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<RedisChatHistoryProvider>());

var searchProvider = new AzureAiSearchContextProvider(
    app.Services.GetRequiredService<IKnowledgeSearch>(),
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<AzureAiSearchContextProvider>());

var attachmentProvider = new AttachmentContextProvider(
    app.Services.GetRequiredService<IAttachmentStore>(),
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<AttachmentContextProvider>());

// Tool index is built AFTER the HTTP server starts (via ApplicationStarted) so the
// startup health probe passes immediately. Chat turns await this task (60 s guard).
var toolIndexTcs = new TaskCompletionSource<IReadOnlyList<(AIFunction Tool, float[] Vector)>>(
    TaskCreationOptions.RunContinuationsAsynchronously);

var topK = cfg.GetSection("DynamicTools").GetValue<int?>("TopK") ?? 5;
var toolSelectionProvider = new DynamicToolSelectionProvider(
    toolIndexTcs.Task,
    embeddingGenerator,
    topK,
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<DynamicToolSelectionProvider>());

var agent = HelpdeskAgentFactory.Create(chatClient, historyProvider, searchProvider, attachmentProvider, toolSelectionProvider);

app.MapAGUI("/agent", agent);
app.MapAttachmentEndpoints();
app.MapTicketEndpoints();

app.MapGet("/agent/usage", async (string? threadId, IRedisService redis) =>
{
    string? json = null;
    if (!string.IsNullOrEmpty(threadId))
        json = await redis.GetAsync($"usage:{threadId}:latest");
    // Fall back to global-latest when threadId is absent or the key doesn't exist.
    if (string.IsNullOrEmpty(json))
        json = await redis.GetAsync("usage:latest");
    return string.IsNullOrEmpty(json) ? Results.NotFound() : Results.Content(json, "application/json");
});

// Initialise tool index after the HTTP server starts so health probes pass immediately.
// Chat turns await toolIndexTcs.Task with a 60 s guard inside DynamicToolSelectionProvider.
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            var toolsProvider = app.Services.GetRequiredService<IMcpToolsProvider>();
            var rawTools = await toolsProvider.GetToolsAsync();
            // Wrap each tool so it auto-reconnects on "Session not found" after McpServer restart.
            var tools = rawTools
                .Select(t => (AIFunction)new RetryingMcpTool(t, toolsProvider))
                .ToList();
            var toolDescriptions = tools.Select(t => $"{t.Name}: {t.Description}").ToList();
            var toolEmbeddings = await embeddingGenerator.GenerateAsync(toolDescriptions);
            var index = tools
                .Zip(toolEmbeddings, (t, e) => (Tool: t, Vector: e.Vector.ToArray()))
                .ToList<(AIFunction Tool, float[] Vector)>();
            toolIndexTcs.TrySetResult(index);
            app.Logger.LogInformation("Tool index ready: {Count} tools embedded.", index.Count);
        }
        catch (Exception ex)
        {
            toolIndexTcs.TrySetException(ex);
            app.Logger.LogError(ex, "Tool index initialisation failed.");
        }
    });
});

app.MapGet("/agent/info", () => Results.Ok(new
{
    service = "HelpdeskAI Agent Host",
    stack = new[]
    {
        "Microsoft.Extensions.AI",
        "Microsoft.Agents.AI.Hosting.AGUI.AspNetCore (MapAGUI)",
        "Microsoft.Agents.AI.OpenAI (AsAIAgent + ChatClientAgentOptions)",
        "ModelContextProtocol",
        "Azure.Storage.Blobs",
    },
}));

app.MapGet("/api/kb/search", async (AzureAiSearchService search, string? q, CancellationToken ct) =>
{
    var results = q is { Length: > 0 }
        ? await search.SearchStructuredAsync(q, ct)
        : await search.BrowseLatestAsync(5, ct);
    return Results.Ok(results);
});

app.MapHealthChecks("/healthz");

await app.RunAsync();

