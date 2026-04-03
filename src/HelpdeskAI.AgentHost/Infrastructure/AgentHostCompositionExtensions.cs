using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using HelpdeskAI.AgentHost.Agents;
using HelpdeskAI.AgentHost.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal static class AgentHostCompositionExtensions
{
    private static readonly string[] TraceSourceNames =
    [
        "HelpdeskAI.AgentHost",
        // Wildcard patterns catch both current "Experimental.*" prefix and any future stable rename.
        "*Microsoft.Extensions.AI",       // gen_ai spans from IChatClient pipeline
        "*Microsoft.Extensions.Agents*",  // MAF agent framework activity sources
        "Microsoft.Agents.AI",
        "Microsoft.Agents.AI.OpenAI",
        "Microsoft.Agents.AI.Workflows",
        "Microsoft.Agents.AI.Hosting.AGUI.AspNetCore"
    ];

    private static readonly string[] MeterNames =
    [
        // Wildcards capture gen_ai.client.token.usage + gen_ai.client.operation.duration
        // regardless of whether the "Experimental." prefix is present in the current version.
        "*Microsoft.Extensions.AI",
        "*Microsoft.Agents.AI"
    ];

    public static TracerProviderBuilder AddHelpdeskTracing(this TracerProviderBuilder tracing)
    {
        // Enrich every span with the current conversation's threadId and user email so that
        // all turns of one conversation are filterable in App Insights Log Analytics by a
        // single `thread.id` custom dimension — even though each HTTP request is its own trace.
        tracing.AddProcessor(new ThreadIdEnrichingProcessor());

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

    public static void UseAgentRequestContext(this WebApplication app)
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

    /// <summary>
    /// OTel span processor that stamps every span with <c>thread.id</c> and
    /// <c>enduser.id</c> so all turns of a conversation are filterable in App Insights
    /// Log Analytics by a single threadId across multiple HTTP requests.
    ///
    /// Both values are read from AsyncLocal stores (<see cref="ThreadIdContext"/> and
    /// <see cref="UserContext"/>) that are set by <c>UseAgentRequestContext</c> middleware
    /// for every agent POST request.  Non-agent spans (health checks, info routes) carry
    /// null values and are left untagged.
    /// </summary>
    private sealed class ThreadIdEnrichingProcessor : BaseProcessor<Activity>
    {
        public override void OnStart(Activity activity)
        {
            if (ThreadIdContext.Current is { } tid)
                activity.SetTag("thread.id", tid);

            if (UserContext.Email is { } email)
                activity.SetTag("enduser.id", email);
        }
    }
}
