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
    private readonly McpServerSettings _cfg = opts.Value;
    private McpClient? _client;
    private IReadOnlyList<AIFunction>? _tools;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<AIFunction>> GetToolsAsync(CancellationToken ct = default)
    {
        if (_tools is not null) return _tools;

        await _lock.WaitAsync(ct);
        try
        {
            if (_tools is not null) return _tools;

            log.LogInformation("Connecting to MCP server at {Endpoint}", _cfg.Endpoint);

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


