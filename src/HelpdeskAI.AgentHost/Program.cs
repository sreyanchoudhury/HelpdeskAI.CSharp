using Azure.AI.OpenAI;
using Azure.Identity;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Agents;
using HelpdeskAI.AgentHost.Endpoints;
using HelpdeskAI.AgentHost.Infrastructure;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
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

IChatClient azureClient = string.IsNullOrWhiteSpace(aiSettings.ApiKey)
	? new AzureOpenAIClient(new Uri(aiSettings.Endpoint), new DefaultAzureCredential())
		  .GetChatClient(aiSettings.ChatDeployment)
		  .AsIChatClient()
	: new AzureOpenAIClient(
			  new Uri(aiSettings.Endpoint),
			  new Azure.AzureKeyCredential(aiSettings.ApiKey))
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
	ConnectionMultiplexer.Connect(
		cfg.GetConnectionString("Redis")
		?? throw new InvalidOperationException("Redis connection string missing")));

builder.Services.AddSingleton<IRedisService, RedisService>();
builder.Services.AddSingleton<AzureAiSearchService>();
builder.Services.AddSingleton<IKnowledgeSearch>(sp => sp.GetRequiredService<AzureAiSearchService>());
builder.Services.AddSingleton<IKnowledgeIngestion>(sp => sp.GetRequiredService<AzureAiSearchService>());
builder.Services.AddSingleton<IMcpToolsProvider, McpToolsProvider>();
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();

// Named HttpClient for internal McpServer calls (base URL derived from MCP endpoint)
var mcpBase = new Uri(cfg["McpServer:Endpoint"] ?? "http://localhost:5100/mcp");
builder.Services.AddHttpClient("McpServer", c =>
	c.BaseAddress = new Uri($"{mcpBase.Scheme}://{mcpBase.Authority}/"));
builder.Services.AddSingleton<IAttachmentStore, RedisAttachmentStore>();
builder.Services.AddSingleton<IDocumentIntelligenceService, DocumentIntelligenceService>();

builder.Services.AddChatClient(azureClient)
	.UseFunctionInvocation()
	.Use((inner, _) => new AGUIHistoryNormalizingClient(inner))
	.UseLogging()
	.UseOpenTelemetry();

var allowedOrigins = cfg["AllowedOrigins"]?.Split(',', StringSplitOptions.RemoveEmptyEntries)
	?? ["http://localhost:3000"];

builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
	p.WithOrigins(allowedOrigins)
	 .AllowAnyHeader()
	 .AllowAnyMethod()));

builder.Services.AddHealthChecks()
	.AddRedis(cfg.GetConnectionString("Redis") ?? "localhost:6379");

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
var toolsProvider = app.Services.GetRequiredService<IMcpToolsProvider>();

var tools = await toolsProvider.GetToolsAsync();

// Pre-embed all tool descriptions once — index is reused for the app's lifetime
var toolDescriptions = tools.Select(t => $"{t.Name}: {t.Description}").ToList();
var toolEmbeddings = await embeddingGenerator.GenerateAsync(toolDescriptions);
var toolIndex = tools
	.Zip(toolEmbeddings, (t, e) => (Tool: t, Vector: e.Vector.ToArray()))
	.ToList();

var historyProvider = new RedisChatHistoryProvider(
	app.Services.GetRequiredService<IRedisService>(),
	chatClient,
	app.Services.GetRequiredService<IOptions<ConversationSettings>>());

var searchProvider = new AzureAiSearchContextProvider(
	app.Services.GetRequiredService<IKnowledgeSearch>(),
	app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<AzureAiSearchContextProvider>());

var attachmentProvider = new AttachmentContextProvider(
	app.Services.GetRequiredService<IAttachmentStore>(),
	app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<AttachmentContextProvider>());

var topK = cfg.GetSection("DynamicTools").GetValue<int?>("TopK") ?? 5;
var toolSelectionProvider = new DynamicToolSelectionProvider(
	toolIndex,
	embeddingGenerator,
	topK,
	app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<DynamicToolSelectionProvider>());

var agent = HelpdeskAgentFactory.Create(chatClient, historyProvider, searchProvider, attachmentProvider, toolSelectionProvider);

app.MapAGUI("/agent", agent);
app.MapAttachmentEndpoints();
app.MapTicketEndpoints();

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

