using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Agents;
using HelpdeskAI.AgentHost.Endpoints;
using HelpdeskAI.AgentHost.Infrastructure;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Agents.AI.Workflows;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.ClientModel.Primitives;

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

// Telemetry:EnableSensitiveData — controls whether gen_ai.input.messages,
// gen_ai.output.messages and other sensitive attributes are captured in traces.
// Set via Azure Container App env var: Telemetry__EnableSensitiveData=true
// Defaults to false so PII is not accidentally captured in production.
var enableSensitiveData = cfg.GetValue<bool>("Telemetry:EnableSensitiveData");
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
builder.Services.AddSingleton<ThreadSideEffectStore>();

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
builder.Services.AddSingleton<EvalRunnerService>();

builder.Services.AddChatClient(azureClient)
    .UseFunctionInvocation()
    .Use((inner, services) => new UsageCapturingChatClient(
        inner,
        services.GetRequiredService<IRedisService>(),
        services.GetRequiredService<IOptions<ConversationSettings>>(),
        services.GetRequiredService<ILoggerFactory>().CreateLogger<UsageCapturingChatClient>()))
    .Use((inner, _) => new AGUIHistoryNormalizingClient(inner))
    .UseLogging()
    .UseOpenTelemetry(configure: c => c.EnableSensitiveData = enableSensitiveData);

// v2 chat client: separate deployment for the multi-agent workflow (falls back to v1 deployment if not set).
var v2Deployment = !string.IsNullOrWhiteSpace(aiSettings.ChatDeploymentV2)
    ? aiSettings.ChatDeploymentV2
    : aiSettings.ChatDeployment;

IChatClient azureClientV2 = string.IsNullOrWhiteSpace(aiSettings.ApiKey)
    ? new AzureOpenAIClient(new Uri(aiSettings.Endpoint), new DefaultAzureCredential(), chatClientOptions)
          .GetChatClient(v2Deployment).AsIChatClient()
    : new AzureOpenAIClient(
              new Uri(aiSettings.Endpoint),
              new Azure.AzureKeyCredential(aiSettings.ApiKey), chatClientOptions)
          .GetChatClient(v2Deployment).AsIChatClient();

// Build the v2 pipeline with the same middleware stack but keyed separately so DI doesn't conflict.
// Additional middleware vs v1:
//   - ThreadIdPreservingChatClient: guards AsyncLocal<ThreadIdContext> across MAF workflow
//     handoff boundaries so attachment/history providers resolve the correct session.
builder.Services.AddKeyedSingleton("v2-chat", (services, _) =>
{
    IChatClient pipeline = new ChatClientBuilder(azureClientV2)
        .UseFunctionInvocation()
        .Use((inner, svc) => new ThreadIdPreservingChatClient(
            inner, svc.GetRequiredService<ILoggerFactory>().CreateLogger<ThreadIdPreservingChatClient>()))
        .Use((inner, svc) => new UsageCapturingChatClient(
            inner,
            svc.GetRequiredService<IRedisService>(),
            svc.GetRequiredService<IOptions<ConversationSettings>>(),
            svc.GetRequiredService<ILoggerFactory>().CreateLogger<UsageCapturingChatClient>()))
        .Use((inner, _) => new AGUIHistoryNormalizingClient(inner))
        .UseLogging()
        .UseOpenTelemetry(configure: c => c.EnableSensitiveData = enableSensitiveData)
        .Build(services);
    return pipeline;
});

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

builder.Services.AddOpenTelemetry()
    .UseAzureMonitor()
    .WithTracing(tracing => tracing.AddHelpdeskTracing())
    .WithMetrics(metrics => metrics.AddHelpdeskMetrics());

// Redis is ephemeral/non-blocking — exclude it from the liveness check so
// /healthz returns 200 whenever the app is running, regardless of Redis state.
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
app.UseAgentRequestContext();

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

// v1 uses LoadAndClearAsync (peek: false) — single agent, clear after injection.
var attachmentProvider = new AttachmentContextProvider(
    app.Services.GetRequiredService<IAttachmentStore>(),
    loggerFactory.CreateLogger<AttachmentContextProvider>());

// v2 diagnostic_agent uses a CLEARING provider (LoadAndClearAsync). This is safe because
// MAF resolves providers per-agent-invocation, and the orchestrator (peek) always runs first
// in a fresh conversation. In continuation scenarios where diagnostic_agent is already active,
// it clears the attachment — which is fine because it's already analyzing it.
var v2DiagnosticAttachmentProvider = new AttachmentContextProvider(
    app.Services.GetRequiredService<IAttachmentStore>(),
    loggerFactory.CreateLogger<AttachmentContextProvider>(),
    peek: false);  // clearing — breaks the loop

// v2 orchestrator uses PEEK (reads without clearing) so it can see the attachment to route.
var v2OrchestratorAttachmentProvider = new AttachmentContextProvider(
    app.Services.GetRequiredService<IAttachmentStore>(),
    loggerFactory.CreateLogger<AttachmentContextProvider>(),
    peek: true);

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

// Agent Skills (FileAgentSkillsProvider): loads behavioral SKILL.md files from the skills/
// directory. Skills are advertised in the system prompt (~100 tokens/skill) and loaded on
// demand via the load_skill tool. The path resolves against AppContext.BaseDirectory so it
// works both in local dev (bin/Debug/skills/) and in the Container Apps image.
var skillsRelPath = cfg["Skills:Path"] ?? "skills";
var skillsAbsPath = Path.IsPathRooted(skillsRelPath)
    ? skillsRelPath
    : Path.Combine(AppContext.BaseDirectory, skillsRelPath);
FileAgentSkillsProvider? skillsProvider = null;
if (Directory.Exists(skillsAbsPath))
{
    skillsProvider = new FileAgentSkillsProvider(skillsAbsPath, loggerFactory: loggerFactory);
    app.Logger.LogInformation("Agent skills loaded from {SkillsPath}", skillsAbsPath);
}
else
{
    app.Logger.LogWarning("Skills directory not found at {SkillsPath} — skills disabled.", skillsAbsPath);
}

var agent = new OpenTelemetryAgent(
    HelpdeskAgentFactory.Create(chatClient, historyProvider, userProvider, memoryProvider,
        turnGuardProvider, searchProvider, attachmentProvider, toolSelectionProvider, skillsProvider),
    "HelpdeskAI.AgentHost")
{
    EnableSensitiveData = enableSensitiveData
};

app.MapAGUI("/agent", agent).RequireAuthorization();
// Demo endpoint — same V1 agent, no auth required (internal feedback only)
app.MapAGUI("/agent/demo", agent);

// === Multi-Agent Workflow — /agent/v2 (additive, single-agent route above is unchanged) ===
// Each specialist gets a DynamicToolSelectionProvider scoped to its own tool subset.
// The shared toolIndexTcs.Task holds ALL embedded tools; AllowedTools filters per specialist.
var ticketToolProvider = new DynamicToolSelectionProvider(
    toolIndexTcs.Task, embeddingGenerator, topK,
    loggerFactory.CreateLogger<DynamicToolSelectionProvider>(),
    allowedTools: TicketAgentFactory.AllowedTools);

var kbToolProvider = new DynamicToolSelectionProvider(
    toolIndexTcs.Task, embeddingGenerator, topK,
    loggerFactory.CreateLogger<DynamicToolSelectionProvider>(),
    allowedTools: KBAgentFactory.AllowedTools);

var incidentToolProvider = new DynamicToolSelectionProvider(
    toolIndexTcs.Task, embeddingGenerator, topK,
    loggerFactory.CreateLogger<DynamicToolSelectionProvider>(),
    allowedTools: IncidentAgentFactory.AllowedTools);

// Frontend tool forwarding: captures CopilotKit render tools (show_ticket_created, etc.)
// from the AG-UI request boundary and provides them to ALL workflow agents. Works around the
// MAF limitation where WorkflowHostAgent passes AgentRunOptions: null to all agents.
var frontendToolProvider = new FrontendToolForwardingProvider(
    loggerFactory.CreateLogger<FrontendToolForwardingProvider>());

var chatClientV2 = app.Services.GetRequiredKeyedService<IChatClient>("v2-chat");

var helpdeskWorkflow = HelpdeskWorkflowFactory.BuildWorkflow(
    chatClientV2,
    userProvider, memoryProvider, turnGuardProvider,
    searchProvider, v2DiagnosticAttachmentProvider,  // diagnostic_agent: CLEAR (breaks loop)
    v2OrchestratorAttachmentProvider,                // orchestrator: PEEK (sees attachment to route)
    frontendToolProvider,
    ticketToolProvider, kbToolProvider, incidentToolProvider,
    skillsProvider,
    enableSensitiveData: enableSensitiveData,
    loggerFactory);

// Use AIAgentBuilder.Use() middleware to capture CopilotKit frontend tools from
// AgentRunOptions BEFORE WorkflowHostAgent drops them (passes null to children).
// This keeps the render tools available to workflow specialists even though they are
// stripped at the child-agent boundary.
var rawWorkflowAgent = helpdeskWorkflow.AsAIAgent("helpdesk-v2", "HelpdeskAI Multi-Agent");
var toolCapturingLogger = loggerFactory.CreateLogger("HelpdeskAI.AgentHost.ToolCapturingMiddleware");
var toolCapturingAgent = new AIAgentBuilder(rawWorkflowAgent)
    .Use(async (messages, session, options, next, ct) =>
    {
        // Capture CopilotKit frontend tools from the AG-UI AgentRunOptions before
        // WorkflowHostAgent strips them by passing null to child agents.
        if (options is ChatClientAgentRunOptions crao
            && crao.ChatOptions?.Tools is { Count: > 0 } tools)
        {
            var frontendTools = FrontendToolForwardingProvider.CaptureFrontendTools(tools);
            toolCapturingLogger.LogInformation(
                "[ToolCapture] Captured {FrontendCount}/{TotalCount} CopilotKit frontend tools from AgentRunOptions",
                frontendTools.Count, tools.Count);
        }
        else
        {
            toolCapturingLogger.LogWarning(
                "[ToolCapture] No tools in AgentRunOptions (options type: {OptionsType})",
                options?.GetType().Name ?? "null");
        }
        await next(messages, session, options, ct);
    })
    .Build(app.Services);

// Outermost wrapper: OpenTelemetryAgent emits the top-level "invoke_agent helpdesk-v2" span.
// Per-specialist child spans come from the OpenTelemetryAgent wrapping inside BuildWorkflow.
var wrappedWorkflowAgent = new OpenTelemetryAgent(toolCapturingAgent, "HelpdeskAI.AgentHost")
{
    EnableSensitiveData = enableSensitiveData
};
app.MapAGUI("/agent/v2", wrappedWorkflowAgent).RequireAuthorization();
app.MapAttachmentEndpoints();
app.MapTicketEndpoints();
app.MapIncidentEndpoints();
// Eval endpoint: enabled in any environment when Evaluation:ApiKey is configured.
// Callers must send the key as X-Eval-Key header. Empty/absent = endpoint not registered.
var evalApiKey = cfg["Evaluation:ApiKey"];
if (!string.IsNullOrWhiteSpace(evalApiKey))
{
    app.MapEvalEndpoints(evalApiKey);
    app.MapEvalResultsEndpoints(evalApiKey, app.Services.GetRequiredService<EvalRunnerService>());
}

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
            var retrySafeLogger = loggerFactory.CreateLogger<RetrySafeSideEffectTool>();
            var sideEffectStore = app.Services.GetRequiredService<ThreadSideEffectStore>();
            var tools = rawTools
                .Select(t =>
                {
                    AIFunction wrapped = new RetryingMcpTool(t, toolsProvider, retryingToolLogger);
                    if (RetrySafeSideEffectTool.ShouldGuard(wrapped.Name))
                        wrapped = new RetrySafeSideEffectTool(wrapped, sideEffectStore, retrySafeLogger);
                    return wrapped;
                })
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
