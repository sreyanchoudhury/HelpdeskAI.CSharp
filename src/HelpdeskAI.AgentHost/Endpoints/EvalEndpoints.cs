using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Agents;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Endpoints;

/// <summary>
/// Thin synchronous evaluation endpoint for the HelpdeskAI.Evaluation test harness.
/// Bypasses the AG-UI SSE layer while still exercising the full IChatClient pipeline
/// (function invocation, usage capture, logging, OpenTelemetry).
///
/// NOT intended for production traffic — no auth, no streaming.
/// Only enabled in non-Production environments (controlled by the caller).
/// </summary>
internal static class EvalEndpoints
{
    internal record EvalRequest(string Message, string? ThreadId = null);
    internal record EvalResponse(string Response, long PromptTokens, long CompletionTokens);

    internal static IEndpointRouteBuilder MapEvalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/agent/eval", HandleAsync)
           .WithName("AgentEval")
           .WithDescription("Synchronous evaluation endpoint for the test harness.");
        return app;
    }

    private static async Task<IResult> HandleAsync(
        EvalRequest req,
        IChatClient chatClient,
        IMcpToolsProvider toolsProvider,
        CancellationToken ct)
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
