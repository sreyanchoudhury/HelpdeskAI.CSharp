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

	public async Task DeleteAsync(string key) =>
		await _db.KeyDeleteAsync(key);
}