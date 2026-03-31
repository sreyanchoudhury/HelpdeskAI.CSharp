using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using HelpdeskAI.AgentHost.Agents;
using HelpdeskAI.AgentHost.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal static class AgentHostCompositionExtensions
{
    private static readonly string[] TraceSourceNames =
    [
        "HelpdeskAI.AgentHost",
        "Microsoft.Extensions.AI",
        "Microsoft.Extensions.AI.OpenAI",
        "Microsoft.Agents.AI",
        "Microsoft.Agents.AI.OpenAI",
        "Microsoft.Agents.AI.Workflows",
        "Microsoft.Agents.AI.Hosting.AGUI.AspNetCore"
    ];

    private static readonly string[] MeterNames =
    [
        "Microsoft.Extensions.AI",
        "Microsoft.Agents.AI"
    ];

    public static TracerProviderBuilder AddHelpdeskTracing(this TracerProviderBuilder tracing)
    {
        foreach (var sourceName in TraceSourceNames)
            tracing.AddSource(sourceName);

        return tracing;
    }

    public static MeterProviderBuilder AddHelpdeskMetrics(this MeterProviderBuilder metrics)
    {
        foreach (var meterName in MeterNames)
            metrics.AddMeter(meterName);

        return metrics;
    }

    public static void UseAgentRequestContext(
        this WebApplication app,
        ActivitySource agentActivitySource)
    {
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        var agentLogger = loggerFactory.CreateLogger("HelpdeskAI.AgentHost");
        var longTermMemoryStore = app.Services.GetRequiredService<LongTermMemoryStore>();

        app.Use(async (context, next) =>
        {
            if (!IsAgentPost(context.Request))
            {
                await next(context);
                return;
            }

            var userName = GetClaimValue(context.User, "name", ClaimTypes.Name);
            var userEmail = GetClaimValue(context.User, "preferred_username", ClaimTypes.Email, "email");
            UserContext.Set(userName, userEmail);

            if (!string.IsNullOrWhiteSpace(userEmail))
                await longTermMemoryStore.UpsertProfileAsync(userEmail, userName, context.RequestAborted);

            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;

            try
            {
                var requestState = ParseRequestState(body);
                TurnStateContext.SetLastUserMessage(requestState.LatestUserMessage);

                var rememberedPreference = TryExtractPreference(requestState.LatestUserMessage);
                if (!string.IsNullOrWhiteSpace(userEmail) && !string.IsNullOrWhiteSpace(rememberedPreference))
                    await longTermMemoryStore.UpsertPreferenceAsync(userEmail, rememberedPreference, context.RequestAborted);

                ThreadIdContext.Set(requestState.ThreadId);
                using var scope = agentLogger.BeginScope(new
                {
                    threadId = requestState.ThreadId,
                    userEmail,
                    latestUserMessage = requestState.LatestUserMessage
                });

                using var agentSpan = StartAgentInvocationSpan(agentActivitySource, context.Request.Path, requestState.ThreadId);
                await next(context);
            }
            finally
            {
                FrontendToolForwardingProvider.Clear();
                ThreadIdContext.Set(null);
                TurnStateContext.Clear();
                UserContext.Clear();
            }
        });
    }

    private static bool IsAgentPost(HttpRequest request) =>
        request.Path.StartsWithSegments("/agent") &&
        HttpMethods.IsPost(request.Method);

    private static string? GetClaimValue(ClaimsPrincipal user, params string[] claimTypes) =>
        claimTypes
            .Select(type => user.FindFirst(type)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static AgentRequestState ParseRequestState(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new AgentRequestState(null, null);

        try
        {
            using var doc = JsonDocument.Parse(body);
            return new AgentRequestState(
                TryGetThreadId(doc.RootElement),
                TryGetLatestUserMessage(doc.RootElement));
        }
        catch (JsonException)
        {
            return new AgentRequestState(null, null);
        }
    }

    private static Activity? StartAgentInvocationSpan(
        ActivitySource activitySource,
        PathString path,
        string? threadId)
    {
        var agentName = path.Value?.Contains("/v2", StringComparison.OrdinalIgnoreCase) == true
            ? "helpdesk-v2"
            : "HelpdeskAgent";

        var activity = activitySource.StartActivity($"invoke_agent {agentName}");
        activity?.SetTag("gen_ai.operation.name", "invoke_agent");
        activity?.SetTag("gen_ai.agent.name", agentName);
        activity?.SetTag("gen_ai.agent.id", agentName);
        activity?.SetTag("gen_ai.system", "openai");
        activity?.SetTag("thread.id", threadId);
        return activity;
    }

    private static string? TryGetThreadId(JsonElement root)
    {
        if (root.TryGetProperty("threadId", out var elem))
            return elem.GetString();

        return null;
    }

    private static string? TryGetLatestUserMessage(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
            return null;

        for (var i = messages.GetArrayLength() - 1; i >= 0; i--)
        {
            var message = messages[i];
            if (message.ValueKind != JsonValueKind.Object)
                continue;

            if (!message.TryGetProperty("role", out var role) ||
                !string.Equals(role.GetString(), "user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!message.TryGetProperty("content", out var content))
                continue;

            var text = ExtractText(content);
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        return null;
    }

    private static string? ExtractText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString();

        if (content.ValueKind == JsonValueKind.Array)
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

        if (content.ValueKind == JsonValueKind.Object)
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

    private static string? TryExtractPreference(string? message)
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

    private sealed record AgentRequestState(string? ThreadId, string? LatestUserMessage);
}
