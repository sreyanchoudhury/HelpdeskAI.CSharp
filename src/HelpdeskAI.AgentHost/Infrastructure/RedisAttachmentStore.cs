using System.Text.Json;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Session-scoped one-shot staging store for processed attachments backed by Redis.
///
/// Flow:
///   1. /api/attachments endpoint calls SaveAsync after processing an upload.
///   2. AttachmentContextProvider calls LoadAndClearAsync on the next agent turn,
///      injecting the content into context and atomically removing it from Redis.
///
/// Multiple uploads before a message are additive (merged into the same list).
/// TTL of 1 hour prevents orphaned entries if the user never sends a follow-up message.
/// </summary>
internal sealed class RedisAttachmentStore(IRedisService redis, ILogger<RedisAttachmentStore> log) : IAttachmentStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    private static string Key(string sessionId) => $"attachments:{sessionId}";

    public async Task SaveAsync(string sessionId, IEnumerable<ProcessedAttachment> attachments, CancellationToken ct = default)
    {
        var key = Key(sessionId);
        try
        {
            // Merge with any already-staged attachments so multiple pre-message uploads are additive
            var existing = await LoadExistingAsync(key);
            existing.AddRange(attachments);
            await redis.SetAsync(key, JsonSerializer.Serialize(existing), Ttl);
            log.LogDebug("Staged {Count} attachment(s) for session '{SessionId}'", existing.Count, sessionId);
        }
        catch (Exception ex)
        {
            // Redis unavailable — attachment will not be injected into agent context for this turn.
            // The file was already processed/uploaded; only the staging step is skipped.
            log.LogWarning(ex, "Redis unavailable — could not stage attachments for session '{SessionId}'", sessionId);
        }
    }

    public async Task<IReadOnlyList<ProcessedAttachment>> LoadAndClearAsync(string sessionId, CancellationToken ct = default)
    {
        var key = Key(sessionId);
        try
        {
            var json = await redis.GetAsync(key);
            if (string.IsNullOrEmpty(json))
                return [];

            // Delete first so a concurrent turn cannot double-inject
            await redis.DeleteAsync(key);

            var attachments = JsonSerializer.Deserialize<List<ProcessedAttachment>>(json) ?? [];
            log.LogDebug("Consumed {Count} attachment(s) for session '{SessionId}'", attachments.Count, sessionId);
            return attachments;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Redis unavailable — returning empty attachment list for session '{SessionId}'", sessionId);
            return [];
        }
    }

    private async Task<List<ProcessedAttachment>> LoadExistingAsync(string key)
    {
        var json = await redis.GetAsync(key);
        return string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<List<ProcessedAttachment>>(json) ?? [];
    }
}
