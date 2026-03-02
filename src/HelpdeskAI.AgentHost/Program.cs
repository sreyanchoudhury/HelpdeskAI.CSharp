using Azure.AI.OpenAI;
using Azure.Identity;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Agents;
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
builder.Services.AddSingleton<IKnowledgeSearch, AzureAiSearchService>();
builder.Services.AddSingleton<IMcpToolsProvider, McpToolsProvider>();

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

var chatClient = app.Services.GetRequiredService<IChatClient>();
var toolsProvider = app.Services.GetRequiredService<IMcpToolsProvider>();

var tools = await toolsProvider.GetToolsAsync();

var conversationSettings = app.Services
	.GetRequiredService<IOptions<ConversationSettings>>().Value;

var historyProvider = new RedisChatHistoryProvider(
	app.Services.GetRequiredService<IRedisService>(),
	chatClient,
	app.Services.GetRequiredService<IOptions<ConversationSettings>>());

var searchProvider = new AzureAiSearchContextProvider(
	app.Services.GetRequiredService<IKnowledgeSearch>(),
	app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<AzureAiSearchContextProvider>());

var agent = HelpdeskAgentFactory.Create(chatClient, tools, historyProvider, searchProvider);

app.MapAGUI("/agent", agent);

app.MapGet("/agent/info", () => Results.Ok(new
{
	service = "HelpdeskAI Agent Host",
	stack = new[]
	{
		"Microsoft.Extensions.AI 10.3.0",
		"Microsoft.Agents.AI.Hosting.AGUI.AspNetCore 1.0.0-preview - MapAGUI",
		"Microsoft.Agents.AI.OpenAI 1.0.0-rc2 - AsAIAgent + ChatClientAgentOptions",
		"ModelContextProtocol 1.0.0",
	},
}));

app.MapHealthChecks("/healthz");

await app.RunAsync();
