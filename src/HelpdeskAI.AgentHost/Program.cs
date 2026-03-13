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

var agent = HelpdeskAgentFactory.Create(chatClient, tools, historyProvider, searchProvider, attachmentProvider);

app.MapAGUI("/agent", agent);
app.MapAttachmentEndpoints();

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

