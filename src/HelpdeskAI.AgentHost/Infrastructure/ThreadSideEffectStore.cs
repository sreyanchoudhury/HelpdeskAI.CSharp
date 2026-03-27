using System.Text.Json;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.Options;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal sealed class ThreadSideEffectStore(
    IRedisService redis,
    IOptions<ConversationSettings> settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TimeSpan _ttl = settings.Value.ThreadTtl;

    public async Task<SideEffectStartResult> TryStartAsync(
        string threadId,
        string toolName,
        string operationKey)
    {
        var redisKey = GetRedisKey(threadId, operationKey);
        var existing = await ReadAsync(redisKey);
        if (existing is not null)
        {
            existing.SuppressionCount++;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveAsync(redisKey, existing);
            return existing.Status switch
            {
                SideEffectOperationStatus.Completed => new SideEffectStartResult(SideEffectStartDisposition.ReuseCompleted, redisKey, existing),
                SideEffectOperationStatus.Pending => new SideEffectStartResult(SideEffectStartDisposition.AlreadyPending, redisKey, existing),
                _ => await StartFreshAsync(redisKey, toolName, operationKey, existing)
            };
        }

        return await StartFreshAsync(redisKey, toolName, operationKey, null);
    }

    public async Task<SideEffectOperationState?> GetAsync(string threadId, string operationKey) =>
        await ReadAsync(GetRedisKey(threadId, operationKey));

    public async Task MarkCompletedAsync(string redisKey, SideEffectOperationState state, string resultPayload)
    {
        state.Status = SideEffectOperationStatus.Completed;
        state.ResultPayload = resultPayload;
        state.LastError = null;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync(redisKey, state);
    }

    public async Task MarkFailedAsync(string redisKey, SideEffectOperationState state, string? error)
    {
        state.Status = SideEffectOperationStatus.Failed;
        state.LastError = string.IsNullOrWhiteSpace(error) ? null : error;
        state.ResultPayload = null;
        state.UpdatedAt = DateTimeOffset.UtcNow;
        await SaveAsync(redisKey, state);
    }

    private async Task<SideEffectStartResult> StartFreshAsync(
        string redisKey,
        string toolName,
        string operationKey,
        SideEffectOperationState? existing)
    {
        var state = existing ?? new SideEffectOperationState
        {
            ToolName = toolName,
            OperationKey = operationKey,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        state.ToolName = toolName;
        state.OperationKey = operationKey;
        state.Status = SideEffectOperationStatus.Pending;
        state.ResultPayload = null;
        state.LastError = null;
        state.UpdatedAt = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            var created = await redis.TrySetAsync(redisKey, JsonSerializer.Serialize(state, JsonOptions), _ttl);
            if (created)
                return new SideEffectStartResult(SideEffectStartDisposition.Started, redisKey, state);

            var winner = await ReadAsync(redisKey);
            if (winner is not null)
            {
                winner.SuppressionCount++;
                winner.UpdatedAt = DateTimeOffset.UtcNow;
                await SaveAsync(redisKey, winner);
                return winner.Status switch
                {
                    SideEffectOperationStatus.Completed => new SideEffectStartResult(SideEffectStartDisposition.ReuseCompleted, redisKey, winner),
                    SideEffectOperationStatus.Pending => new SideEffectStartResult(SideEffectStartDisposition.AlreadyPending, redisKey, winner),
                    _ => await StartFreshAsync(redisKey, toolName, operationKey, winner)
                };
            }
        }

        await SaveAsync(redisKey, state);
        return new SideEffectStartResult(SideEffectStartDisposition.Started, redisKey, state);
    }

    private async Task<SideEffectOperationState?> ReadAsync(string redisKey)
    {
        var json = await redis.GetAsync(redisKey);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<SideEffectOperationState>(json, JsonOptions);
    }

    private async Task SaveAsync(string redisKey, SideEffectOperationState state) =>
        await redis.SetAsync(redisKey, JsonSerializer.Serialize(state, JsonOptions), _ttl);

    private static string GetRedisKey(string threadId, string operationKey) =>
        $"sideeffect:{threadId}:{operationKey}";
}

internal enum SideEffectOperationStatus
{
    Pending,
    Completed,
    Failed,
}

internal enum SideEffectStartDisposition
{
    Started,
    ReuseCompleted,
    AlreadyPending,
}

internal sealed class SideEffectOperationState
{
    public string ToolName { get; set; } = string.Empty;
    public string OperationKey { get; set; } = string.Empty;
    public SideEffectOperationStatus Status { get; set; }
    public string? ResultPayload { get; set; }
    public string? LastError { get; set; }
    public int SuppressionCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed record SideEffectStartResult(
    SideEffectStartDisposition Disposition,
    string RedisKey,
    SideEffectOperationState State);
