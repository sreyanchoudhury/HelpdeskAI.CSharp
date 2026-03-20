using System.Text.Json;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.Options;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal sealed class LongTermMemoryStore(
    IRedisService redis,
    IOptions<LongTermMemorySettings> settings)
{
    private readonly LongTermMemorySettings _settings = settings.Value;

    public async Task UpsertProfileAsync(string email, string? name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return;

        var existing = await GetProfileAsync(email, ct);
        var profile = new LongTermUserProfile(
            Email: email,
            Name: string.IsNullOrWhiteSpace(name) ? existing?.Name : name,
            LastSeenAt: DateTimeOffset.UtcNow,
            Preferences: existing?.Preferences ?? []);

        await redis.SetAsync(ProfileKey(email), JsonSerializer.Serialize(profile), _settings.ProfileTtl);
    }

    public async Task<LongTermUserProfile?> GetProfileAsync(string email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var json = await redis.GetAsync(ProfileKey(email));
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<LongTermUserProfile>(json);
    }

    public async Task UpsertPreferenceAsync(string email, string preference, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(preference))
            return;

        var existing = await GetProfileAsync(email, ct)
            ?? new LongTermUserProfile(email, null, DateTimeOffset.UtcNow, []);

        var preferences = existing.Preferences
            .Append(preference.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        var updated = existing with
        {
            Preferences = preferences,
            LastSeenAt = DateTimeOffset.UtcNow
        };

        await redis.SetAsync(ProfileKey(email), JsonSerializer.Serialize(updated), _settings.ProfileTtl);
    }

    private static string ProfileKey(string email) => $"ltm:{email.Trim().ToLowerInvariant()}:profile";
}

internal sealed record LongTermUserProfile(string Email, string? Name, DateTimeOffset LastSeenAt, string[] Preferences);
