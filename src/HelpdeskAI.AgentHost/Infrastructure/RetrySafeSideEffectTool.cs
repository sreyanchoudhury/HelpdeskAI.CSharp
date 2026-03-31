using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal sealed class RetrySafeSideEffectTool : DelegatingAIFunction
{
    private static readonly HashSet<string> SupportedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "create_ticket",
        "index_kb_article",
    };
    private const int PendingWaitAttempts = 20;
    private static readonly TimeSpan PendingWaitDelay = TimeSpan.FromMilliseconds(300);

    private readonly AIFunction _inner;
    private readonly ThreadSideEffectStore _store;
    private readonly string _toolName;
    private readonly ILogger _logger;

    public RetrySafeSideEffectTool(AIFunction inner, ThreadSideEffectStore store, ILogger logger) : base(inner)
    {
        _inner = inner;
        _store = store;
        _toolName = inner.Name;
        _logger = logger;
    }

    public static bool ShouldGuard(string toolName) => SupportedTools.Contains(toolName);

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        if (!ShouldGuard(_toolName) ||
            ThreadIdContext.Current is not { Length: > 0 } threadId ||
            !TryBuildOperationKey(arguments, out var operationKey))
        {
            return await _inner.InvokeAsync(arguments, cancellationToken);
        }

        var start = await _store.TryStartAsync(threadId, _toolName, operationKey);
        if (start.Disposition == SideEffectStartDisposition.ReuseCompleted &&
            !string.IsNullOrWhiteSpace(start.State.ResultPayload))
        {
            _logger.LogInformation(
                "Reusing completed side effect - toolName: {ToolName}, threadId: {ThreadId}, operationKey: {OperationKey}, suppressionCount: {SuppressionCount}, outcome: {Outcome}.",
                _toolName, threadId, operationKey, start.State.SuppressionCount, "reused_completed");
            return start.State.ResultPayload;
        }

        if (start.Disposition == SideEffectStartDisposition.AlreadyPending)
        {
            var completed = await WaitForCompletionAsync(threadId, operationKey, cancellationToken);
            if (completed is { Status: SideEffectOperationStatus.Completed, ResultPayload: { Length: > 0 } })
            {
                _logger.LogInformation(
                    "Reusing completed side effect after pending wait - toolName: {ToolName}, threadId: {ThreadId}, operationKey: {OperationKey}, suppressionCount: {SuppressionCount}, outcome: {Outcome}.",
                    _toolName, threadId, operationKey, completed.SuppressionCount, "reused_after_pending");
                return completed.ResultPayload;
            }

            _logger.LogWarning(
                "Suppressed duplicate side effect while original operation remained pending - toolName: {ToolName}, threadId: {ThreadId}, operationKey: {OperationKey}, suppressionCount: {SuppressionCount}, outcome: {Outcome}.",
                _toolName, threadId, operationKey, start.State.SuppressionCount, "suppressed_pending");
            return BuildPendingResponse(operationKey, start.State);
        }

        try
        {
            var rawResult = await _inner.InvokeAsync(arguments, cancellationToken);
            var payload = ToPayloadString(rawResult);
            if (IsSuccessfulPayload(payload))
            {
                await _store.MarkCompletedAsync(start.RedisKey, start.State, payload);
                _logger.LogInformation(
                    "Side effect completed - toolName: {ToolName}, threadId: {ThreadId}, operationKey: {OperationKey}, suppressionCount: {SuppressionCount}, outcome: {Outcome}.",
                    _toolName, threadId, operationKey, start.State.SuppressionCount, "new_write");
            }
            else
            {
                await _store.MarkFailedAsync(start.RedisKey, start.State, payload);
                _logger.LogWarning(
                    "Side effect returned a non-success payload - toolName: {ToolName}, threadId: {ThreadId}, operationKey: {OperationKey}, outcome: {Outcome}.",
                    _toolName, threadId, operationKey, "tool_reported_failure");
            }

            return payload;
        }
        catch (Exception ex)
        {
            await _store.MarkFailedAsync(start.RedisKey, start.State, ex.Message);
            _logger.LogError(
                ex,
                "Side effect failed - toolName: {ToolName}, threadId: {ThreadId}, operationKey: {OperationKey}, outcome: {Outcome}.",
                _toolName, threadId, operationKey, "exception");
            throw;
        }
    }

    private async Task<SideEffectOperationState?> WaitForCompletionAsync(
        string threadId,
        string operationKey,
        CancellationToken cancellationToken)
    {
        for (var i = 0; i < PendingWaitAttempts; i++)
        {
            await Task.Delay(PendingWaitDelay, cancellationToken);
            var state = await _store.GetAsync(threadId, operationKey);
            if (state is null || state.Status != SideEffectOperationStatus.Pending)
                return state;
        }

        return await _store.GetAsync(threadId, operationKey);
    }

    private bool TryBuildOperationKey(AIFunctionArguments arguments, out string operationKey)
    {
        operationKey = string.Empty;

        if (_toolName.Equals("create_ticket", StringComparison.OrdinalIgnoreCase))
        {
            var title = Normalize(GetString(arguments, "title"));
            var description = Normalize(GetString(arguments, "description"));
            var category = Normalize(GetString(arguments, "category"));
            var requestedBy = Normalize(GetString(arguments, "requestedBy"));

            if (string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(description) ||
                string.IsNullOrWhiteSpace(requestedBy))
            {
                return false;
            }

            operationKey = HashOperationKey(_toolName, title, description, category, requestedBy);
            return true;
        }

        if (_toolName.Equals("index_kb_article", StringComparison.OrdinalIgnoreCase))
        {
            var title = Normalize(GetString(arguments, "title"));
            var content = Normalize(GetString(arguments, "content"));
            var category = Normalize(GetString(arguments, "category"));

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
                return false;

            operationKey = HashOperationKey(_toolName, title, HashContent(content), category);
            return true;
        }

        return false;
    }

    private string BuildPendingResponse(string operationKey, SideEffectOperationState state) =>
        JsonSerializer.Serialize(new
        {
            status = "pending",
            tool = _toolName,
            operationKey,
            suppressionCount = state.SuppressionCount,
            message = _toolName switch
            {
                "create_ticket" => "A matching ticket creation request is already in progress for this conversation. Reuse the existing ticket if it appears, and continue with any remaining non-creation steps.",
                "index_kb_article" => "A matching KB indexing request is already in progress for this conversation. Reuse the existing article if it appears, and continue with any remaining non-indexing steps.",
                _ => "A matching side-effect request is already in progress for this conversation. Continue with any remaining non-duplicate steps."
            }
        });

    private bool IsSuccessfulPayload(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("id", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetString(AIFunctionArguments arguments, string name)
    {
        if (!arguments.TryGetValue(name, out var value) || value is null)
            return null;

        return value switch
        {
            string s => s,
            JsonElement elem when elem.ValueKind == JsonValueKind.String => elem.GetString(),
            JsonElement elem => elem.ToString(),
            _ => value.ToString()
        };
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var parts = value
            .Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).ToLowerInvariant();
    }

    private static string HashContent(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    private static string HashOperationKey(string toolName, params string[] parts) =>
        HashContent($"{toolName}|{string.Join('|', parts)}");

    private static string ToPayloadString(object? result) =>
        result switch
        {
            null => string.Empty,
            string s => s,
            JsonElement elem when elem.ValueKind is JsonValueKind.Object or JsonValueKind.Array => elem.GetRawText(),
            JsonElement elem => elem.ToString(),
            _ => JsonSerializer.Serialize(result)
        };
}
