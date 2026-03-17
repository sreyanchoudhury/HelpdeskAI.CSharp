using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal sealed class McpToolsProvider(
    IOptions<McpServerSettings> opts,
    ILogger<McpToolsProvider> log) : IMcpToolsProvider, IAsyncDisposable
{
    // Azure Container Apps closes idle SSE streams after ~4 minutes.
    // Proactively reconnect at 3.5 minutes to avoid mid-call session expiry.
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(3.5);

    private readonly McpServerSettings _cfg = opts.Value;
    private McpClient? _client;
    private IReadOnlyList<AIFunction>? _tools;
    private DateTimeOffset _connectedAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<AIFunction>> GetToolsAsync(CancellationToken ct = default)
    {
        // Fast path — only skip re-init if tools are loaded AND session is still fresh.
        if (_tools is not null && DateTimeOffset.UtcNow - _connectedAt < SessionTtl)
            return _tools;

        await _lock.WaitAsync(ct);
        try
        {
            // Re-check inside lock — another thread may have refreshed already.
            if (_tools is not null && DateTimeOffset.UtcNow - _connectedAt < SessionTtl)
                return _tools;

            if (_tools is not null)
                log.LogInformation("MCP session TTL reached — proactively reconnecting.");
            else
                log.LogInformation("Connecting to MCP server at {Endpoint}", _cfg.Endpoint);

            // Dispose stale client before reconnecting.
            if (_client is IAsyncDisposable d) await d.DisposeAsync();
            _client = null;
            _tools  = null;

            // HttpClientTransport + McpClient.CreateAsync are the correct MCP 1.x client APIs.
            // The transport manages the HTTP+SSE connection; CreateAsync performs the protocol
            // handshake and returns a ready-to-use McpClient.
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(_cfg.Endpoint)
            });

            _client = await McpClient.CreateAsync(
                clientTransport: transport,
                cancellationToken: ct);

            var toolList = await _client.ListToolsAsync(cancellationToken: ct);
            _tools = [..toolList.OfType<AIFunction>()];
            _connectedAt = DateTimeOffset.UtcNow;

            log.LogInformation(
                "Loaded {Count} MCP tools: {Names}",
                _tools.Count,
                string.Join(", ", _tools.Select(t => t.Name)));

            return _tools;
        }
        catch (Exception ex)
        {
            log.LogError(ex,
                "Failed to load MCP tools from {Endpoint}. " +
                "Ensure HelpdeskAI.McpServer is running. Agent will continue without tools.",
                _cfg.Endpoint);
            // Do NOT cache the failure — leave _tools null so the next request retries.
            return [];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<AIFunction>> RefreshAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            log.LogInformation("Reconnecting to MCP server after session expiry...");

            if (_client is IAsyncDisposable d) await d.DisposeAsync();
            _client = null;
            _tools = null;

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(_cfg.Endpoint)
            });

            _client = await McpClient.CreateAsync(clientTransport: transport, cancellationToken: ct);
            var toolList = await _client.ListToolsAsync(cancellationToken: ct);
            _tools = [..toolList.OfType<AIFunction>()];
            _connectedAt = DateTimeOffset.UtcNow;

            log.LogInformation("Reconnected. Loaded {Count} MCP tools.", _tools.Count);
            return _tools;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to reconnect to MCP server at {Endpoint}.", _cfg.Endpoint);
            _tools = [];
            return _tools;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lock.Dispose();
        if (_client is IAsyncDisposable d) await d.DisposeAsync();
    }
}


