using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Agents;
using HelpdeskAI.AgentHost.Endpoints;
using HelpdeskAI.AgentHost.Infrastructure;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.ClientModel.Primitives;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

builder.Services.Configure<AzureOpenAiSettings>(cfg.GetSection("AzureOpenAI"));
builder.Services.Configure<AzureAiSearchSettings>(cfg.GetSection("AzureAISearch"));
builder.Services.Configure<McpServerSettings>(cfg.GetSection("McpServer"));
builder.Services.Configure<ConversationSettings>(cfg.GetSection("Conversation"));
builder.Services.Configure<AzureBlobStorageSettings>(cfg.GetSection("AzureBlobStorage"));
builder.Services.Configure<DocumentIntelligenceSettings>(cfg.GetSection("DocumentIntelligence"));
builder.Services.Configure<EntraAuthSettings>(cfg.GetSection("EntraAuth"));
builder.Services.Configure<LongTermMemorySettings>(cfg.GetSection("LongTermMemory"));

var aiSettings = cfg.GetSection("AzureOpenAI").Get<AzureOpenAiSettings>()
    ?? throw new InvalidOperationException("AzureOpenAI config section missing");
var authSettings = cfg.GetSection("EntraAuth").Get<EntraAuthSettings>()
    ?? throw new InvalidOperationException("EntraAuth config section missing");

if (string.IsNullOrWhiteSpace(authSettings.TenantId) ||
    string.IsNullOrWhiteSpace(authSettings.ClientId))
{
    throw new InvalidOperationException("EntraAuth:TenantId and EntraAuth:ClientId are required.");
}

var validAudience = !string.IsNullOrWhiteSpace(authSettings.Audience)
    ? authSettings.Audience
    : $"api://{authSettings.ClientId}";
var authority = !string.IsNullOrWhiteSpace(authSettings.Authority)
    ? authSettings.Authority.TrimEnd('/')
    : $"https://login.microsoftonline.com/{authSettings.TenantId}/v2.0";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidAudiences = new[] { validAudience, authSettings.ClientId }.Distinct(),
            NameClaimType = "name",
        };
    });
builder.Services.AddAuthorization();

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
builder.Services.AddSingleton<LongTermMemoryStore>();

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
        services.GetRequiredService<IOptions<ConversationSettings>>(),
        services.GetRequiredService<ILoggerFactory>().CreateLogger<UsageCapturingChatClient>()))
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
app.UseAuthentication();
app.UseAuthorization();

// Cache ILoggerFactory from DI once — used throughout startup and middleware.
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var agentLogger = loggerFactory.CreateLogger("HelpdeskAI.AgentHost");
var longTermMemoryStore = app.Services.GetRequiredService<LongTermMemoryStore>();

static string? GetClaimValue(ClaimsPrincipal user, params string[] claimTypes) =>
    claimTypes
        .Select(type => user.FindFirst(type)?.Value)
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

static string? TryGetThreadId(System.Text.Json.JsonElement root)
{
    if (root.TryGetProperty("threadId", out var elem))
        return elem.GetString();
    return null;
}

static string? TryGetLatestUserMessage(System.Text.Json.JsonElement root)
{
    if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != System.Text.Json.JsonValueKind.Array)
        return null;

    for (var i = messages.GetArrayLength() - 1; i >= 0; i--)
    {
        var message = messages[i];
        if (message.ValueKind != System.Text.Json.JsonValueKind.Object)
            continue;
        if (!message.TryGetProperty("role", out var role) || !string.Equals(role.GetString(), "user", StringComparison.OrdinalIgnoreCase))
            continue;
        if (!message.TryGetProperty("content", out var content))
            continue;

        var text = ExtractText(content);
        if (!string.IsNullOrWhiteSpace(text))
            return text;
    }

    return null;
}

static string? ExtractText(System.Text.Json.JsonElement content)
{
    if (content.ValueKind == System.Text.Json.JsonValueKind.String)
        return content.GetString();

    if (content.ValueKind == System.Text.Json.JsonValueKind.Array)
    {
        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            var text = ExtractText(item);
            if (!string.IsNullOrWhiteSpace(text))
                parts.Add(text);
        }
        return parts.Count == 0 ? null : string.Join("\n", parts);
    }

    if (content.ValueKind == System.Text.Json.JsonValueKind.Object)
    {
        if (content.TryGetProperty("text", out var text))
            return ExtractText(text);
        if (content.TryGetProperty("content", out var nested))
            return ExtractText(nested);
        if (content.TryGetProperty("value", out var value))
            return ExtractText(value);
    }

    return null;
}

static string? TryExtractPreference(string? message)
{
    if (string.IsNullOrWhiteSpace(message))
        return null;

    var text = message.Trim();
    const string rememberPrefix = "remember that ";
    if (text.StartsWith(rememberPrefix, StringComparison.OrdinalIgnoreCase))
        return text[rememberPrefix.Length..].Trim().TrimEnd('.');

    const string rememberPreferencePrefix = "please remember that ";
    if (text.StartsWith(rememberPreferencePrefix, StringComparison.OrdinalIgnoreCase))
        return text[rememberPreferencePrefix.Length..].Trim().TrimEnd('.');

    return null;
}

// Populate ThreadIdContext before the request is handled so history providers
// can key Redis by AG-UI threadId without access to the (always-null) AgentSession.
// Also opens a request-level BeginScope(threadId) so every log line during the turn
// carries threadId as customDimensions.threadId in App Insights traces.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/agent") &&
        context.Request.Method == "POST")
    {
        var userName = GetClaimValue(context.User, "name", ClaimTypes.Name);
        var userEmail = GetClaimValue(context.User, "preferred_username", ClaimTypes.Email, "email");
        UserContext.Set(userName, userEmail);
        if (!string.IsNullOrWhiteSpace(userEmail))
            await longTermMemoryStore.UpsertProfileAsync(userEmail, userName, context.RequestAborted);

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        string? threadId = null;
        string? latestUserMessage = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            threadId = TryGetThreadId(doc.RootElement);
            latestUserMessage = TryGetLatestUserMessage(doc.RootElement);
            TurnStateContext.SetLastUserMessage(latestUserMessage);

            var rememberedPreference = TryExtractPreference(latestUserMessage);
            if (!string.IsNullOrWhiteSpace(userEmail) && !string.IsNullOrWhiteSpace(rememberedPreference))
                await longTermMemoryStore.UpsertPreferenceAsync(userEmail, rememberedPreference, context.RequestAborted);

            ThreadIdContext.Set(threadId);
            using var scope = agentLogger.BeginScope(new { threadId, userEmail, latestUserMessage });
            await next(context);
            return;
        }
        finally
        {
            ThreadIdContext.Set(null);
            TurnStateContext.Clear();
            UserContext.Clear();
        }
    }

    await next(context);
});

var chatClient = app.Services.GetRequiredService<IChatClient>();

var historyProvider = new RedisChatHistoryProvider(
    app.Services.GetRequiredService<IRedisService>(),
    chatClient,
    app.Services.GetRequiredService<IOptions<ConversationSettings>>(),
    loggerFactory.CreateLogger<RedisChatHistoryProvider>());

var userProvider = new UserContextProvider();
var memoryProvider = new LongTermMemoryContextProvider(
    app.Services.GetRequiredService<LongTermMemoryStore>(),
    loggerFactory.CreateLogger<LongTermMemoryContextProvider>());
var turnGuardProvider = new TurnGuardContextProvider();

var searchProvider = new AzureAiSearchContextProvider(
    app.Services.GetRequiredService<IKnowledgeSearch>(),
    loggerFactory.CreateLogger<AzureAiSearchContextProvider>());

var attachmentProvider = new AttachmentContextProvider(
    app.Services.GetRequiredService<IAttachmentStore>(),
    loggerFactory.CreateLogger<AttachmentContextProvider>());

// Tool index is built AFTER the HTTP server starts (via ApplicationStarted) so the
// startup health probe passes immediately. Chat turns await this task (60 s guard).
var toolIndexTcs = new TaskCompletionSource<IReadOnlyList<(AIFunction Tool, float[] Vector)>>(
    TaskCreationOptions.RunContinuationsAsynchronously);

var topK = cfg.GetSection("DynamicTools").GetValue<int?>("TopK") ?? 5;
var toolSelectionProvider = new DynamicToolSelectionProvider(
    toolIndexTcs.Task,
    embeddingGenerator,
    topK,
    loggerFactory.CreateLogger<DynamicToolSelectionProvider>());

var agent = HelpdeskAgentFactory.Create(chatClient, historyProvider, userProvider, memoryProvider, turnGuardProvider, searchProvider, attachmentProvider, toolSelectionProvider);

app.MapAGUI("/agent", agent).RequireAuthorization();
app.MapAttachmentEndpoints();
app.MapTicketEndpoints();
// Eval endpoint is not safe for production — exposes synchronous agent execution without auth.
if (!app.Environment.IsProduction()) app.MapEvalEndpoints();

app.MapGet("/agent/usage", async (string? threadId, IRedisService redis) =>
{
    if (string.IsNullOrWhiteSpace(threadId))
        return Results.BadRequest(new { error = "threadId is required" });
    var json = await redis.GetAsync($"usage:{threadId}:latest");
    return string.IsNullOrEmpty(json) ? Results.NotFound() : Results.Content(json, "application/json");
}).RequireAuthorization();

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
            // Passes the shared logger so each RetryingMcpTool emits structured audit traces.
            var retryingToolLogger = loggerFactory.CreateLogger<RetryingMcpTool>();
            var tools = rawTools
                .Select(t => (AIFunction)new RetryingMcpTool(t, toolsProvider, retryingToolLogger))
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
}).RequireAuthorization();

app.MapHealthChecks("/healthz");

await app.RunAsync();
