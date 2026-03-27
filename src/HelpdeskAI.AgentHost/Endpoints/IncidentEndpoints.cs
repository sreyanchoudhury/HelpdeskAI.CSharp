using System.Text.Json;
using HelpdeskAI.AgentHost.Abstractions;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Endpoints;

internal static class IncidentEndpoints
{
    internal static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/incidents/active", HandleActiveIncidentsAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> HandleActiveIncidentsAsync(
        IMcpToolsProvider toolsProvider,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("HelpdeskAI.AgentHost.Endpoints.IncidentEndpoints");

        try
        {
            var tools = await toolsProvider.GetToolsAsync(ct);
            var tool = tools.FirstOrDefault(t => t.Name.Equals("get_active_incidents", StringComparison.OrdinalIgnoreCase));
            if (tool is null)
                return Results.Json(new { error = "Incident tool unavailable" }, statusCode: 503);

            var raw = await tool.InvokeAsync(new AIFunctionArguments(), ct);
            var normalized = TryNormalizeIncidentPayload(raw, out var payload)
                ? payload
                : new { count = 0, incidents = Array.Empty<object>(), checkedAt = DateTimeOffset.UtcNow };

            return Results.Json(normalized, statusCode: 200);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Incident feed unavailable when invoking get_active_incidents");
            return Results.Json(new { error = "Incident feed unavailable" }, statusCode: 502);
        }
    }

    private static bool TryNormalizeIncidentPayload(object? raw, out object payload)
    {
        payload = default!;

        if (!TryGetIncidentRoot(raw, out var root))
            return false;

        if (root.ValueKind != JsonValueKind.Object)
            return false;

        var incidents = root.TryGetProperty("incidents", out var incidentsElement) &&
                        incidentsElement.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<object[]>(incidentsElement.GetRawText()) ?? []
            : [];

        var count = root.TryGetProperty("count", out var countElement) &&
                    countElement.TryGetInt32(out var parsedCount)
            ? parsedCount
            : incidents.Length;

        var checkedAt = root.TryGetProperty("checkedAt", out var checkedAtElement)
            ? checkedAtElement.ToString()
            : DateTimeOffset.UtcNow.ToString("O");

        payload = new
        {
            count,
            incidents,
            checkedAt,
        };

        return true;
    }

    private static bool TryGetIncidentRoot(object? raw, out JsonElement root)
    {
        root = default;

        if (raw is null)
            return false;

        if (raw is JsonElement element)
            return TryGetIncidentRoot(element, out root);

        if (raw is string text)
            return TryParseJson(text, out root);

        var contentText = TryExtractObjectText(raw);
        if (!string.IsNullOrWhiteSpace(contentText))
            return TryParseJson(contentText, out root);

        return TryParseJson(JsonSerializer.Serialize(raw), out root);
    }

    private static bool TryGetIncidentRoot(JsonElement element, out JsonElement root)
    {
        root = default;

        if (element.ValueKind == JsonValueKind.String)
            return TryParseJson(element.GetString(), out root);

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("incidents", out _))
        {
            root = element;
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("content", out var contentElement) &&
            TryExtractContentText(contentElement, out var contentText))
        {
            return TryParseJson(contentText, out root);
        }

        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() == 1)
            return TryGetIncidentRoot(element[0], out root);

        if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            root = element;
            return true;
        }

        return false;
    }

    private static string? TryExtractObjectText(object raw)
    {
        var type = raw.GetType();
        var text = type.GetProperty("Text")?.GetValue(raw) as string;
        if (!string.IsNullOrWhiteSpace(text))
            return text;

        var value = type.GetProperty("Value")?.GetValue(raw);
        if (value is string valueText && !string.IsNullOrWhiteSpace(valueText))
            return valueText;

        var content = type.GetProperty("Content")?.GetValue(raw);
        return content is null ? null : TryExtractContentText(content);
    }

    private static string? TryExtractContentText(object content)
    {
        return content switch
        {
            JsonElement element when TryExtractContentText(element, out var text) => text,
            string text => text,
            System.Collections.IEnumerable items => items
                .Cast<object?>()
                .Select(item => item is null ? null : TryExtractContentText(item))
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)),
            _ => null
        };
    }

    private static bool TryExtractContentText(JsonElement content, out string? text)
    {
        text = null;

        if (content.ValueKind == JsonValueKind.String)
        {
            text = content.GetString();
            return !string.IsNullOrWhiteSpace(text);
        }

        if (content.ValueKind == JsonValueKind.Object &&
            content.TryGetProperty("text", out var textElement))
        {
            text = textElement.GetString();
            return !string.IsNullOrWhiteSpace(text);
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (TryExtractContentText(item, out text))
                    return true;
            }
        }

        return false;
    }

    private static bool TryParseJson(string? text, out JsonElement root)
    {
        root = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(text);
            root = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
