using HelpdeskAI.AgentHost.Abstractions;
using StackExchange.Redis;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal sealed class RedisService(IConnectionMultiplexer multiplexer) : IRedisService
{
    private readonly IDatabase _db = multiplexer.GetDatabase();

    public async Task<string?> GetAsync(string key)
    {
        var value = await _db.StringGetAsync(key);
        return value.HasValue ? value.ToString() : null;
    }

    public async Task SetAsync(string key, string value, TimeSpan ttl) =>
        await _db.StringSetAsync(key, value, ttl);

    public async Task<bool> TrySetAsync(string key, string value, TimeSpan ttl) =>
        await _db.StringSetAsync(key, value, ttl, when: When.NotExists);

    public async Task DeleteAsync(string key) =>
        await _db.KeyDeleteAsync(key);

    public async Task<long> DeleteByPrefixAsync(string prefix)
    {
        var deleted = 0L;
        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            var server = multiplexer.GetServer(endpoint);
            if (!server.IsConnected)
                continue;

            var batch = new List<RedisKey>();
            foreach (var key in server.Keys(pattern: $"{prefix}*"))
            {
                batch.Add(key);
                if (batch.Count < 256)
                    continue;

                deleted += await _db.KeyDeleteAsync(batch.ToArray());
                batch.Clear();
            }

            if (batch.Count > 0)
                deleted += await _db.KeyDeleteAsync(batch.ToArray());
        }

        return deleted;
    }
}
