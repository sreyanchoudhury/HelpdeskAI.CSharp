using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Agents;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Endpoints;

/// <summary>
/// Thin synchronous evaluation endpoint for the HelpdeskAI.Evaluation test harness.
/// Bypasses the AG-UI SSE layer while still exercising the full IChatClient pipeline
/// (function invocation, usage capture, logging, OpenTelemetry).
///
/// Enabled in any environment when <c>Evaluation:ApiKey</c> is configured.
/// Callers must send the key in the <c>X-Eval-Key</c> request header.
/// </summary>
internal static class EvalEndpoints
{
    internal const string EvalKeyHeader = "X-Eval-Key";

    internal record EvalRequest(string Message, string? ThreadId = null);
    internal record EvalResponse(string Response, long PromptTokens, long CompletionTokens);

    internal static IEndpointRouteBuilder MapEvalEndpoints(
        this IEndpointRouteBuilder app, string apiKey)
    {
        app.MapPost("/agent/eval", async (HttpContext ctx, EvalRequest req,
            IChatClient chatClient, IMcpToolsProvider toolsProvider, CancellationToken ct) =>
        {
            if (!ctx.Request.Headers.TryGetValue(EvalKeyHeader, out var sentKey)
                || sentKey != apiKey)
                return Results.Unauthorized();

            return await HandleAsync(req, chatClient, toolsProvider, ct);
        })
        .WithName("AgentEval")
        .WithDescription("Synchronous eval endpoint — requires X-Eval-Key header.");
        return app;
    }

    private static async Task<IResult> HandleAsync(
        EvalRequest req, IChatClient chatClient,
        IMcpToolsProvider toolsProvider, CancellationToken ct)
    {
        // Fetch raw MCP tools — UseFunctionInvocation middleware in the DI pipeline
        // will invoke them automatically when the LLM produces tool calls.
        var tools = (await toolsProvider.GetToolsAsync(ct)).Cast<AITool>().ToList();

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, HelpdeskAgentFactory.BaseInstructions),
            new(ChatRole.User, req.Message),
        };

        var options = new ChatOptions { Tools = tools };
        var response = await chatClient.GetResponseAsync(messages, options, ct);

        return Results.Json(new EvalResponse(
            response.Text ?? string.Empty,
            response.Usage?.InputTokenCount ?? 0,
            response.Usage?.OutputTokenCount ?? 0));
    }
}
