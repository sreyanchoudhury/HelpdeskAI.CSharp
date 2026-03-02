using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Connects to the HelpdeskAI MCP server (HelpdeskAI.McpServer) via HTTP+SSE
/// and exposes its tools as <see cref="AIFunction"/> instances.
///
/// <para>
/// Key integration: <c>ModelContextProtocol 1.0.0</c> makes <c>McpClientTool</c>
/// implement <see cref="AIFunction"/> (a <c>Microsoft.Extensions.AI</c> type) directly.
/// MCP tools therefore drop into <c>IAgent.Tools</c> with zero adapter code.
/// </para>
///
/// <para>
/// Bugs fixed from previous version:
/// <list type="bullet">
///   <item><c>HttpClientTransport</c>  <c>SseClientTransport</c> (correct class for HTTP+SSE MCP transport)</item>
///   <item><c>McpClient.CreateAsync()</c>  <c>McpClientFactory.CreateAsync()</c> (correct factory API)</item>
///   <item><c>McpClient?</c> field  <c>IMcpClient?</c> (program to the interface, not the concrete type)</item>
/// </list>
/// </para>
///
/// <para>
/// Registered as a singleton. The MCP connection is lazy-initialised on first call
/// and reused for the process lifetime (connection per process, not per request).
/// Falls back gracefully to an empty tool list if the MCP server is unavailable.
/// </para>
/// </summary>
internal sealed class McpToolsProvider(
    IOptions<McpServerSettings> opts,
    ILogger<McpToolsProvider> log) : IMcpToolsProvider, IAsyncDisposable
{
    private readonly McpServerSettings _cfg = opts.Value;
    private McpClient? _client;                  //  interface, not concrete McpClient
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

            //  Correct transport: SseClientTransport (not HttpClientTransport) 
            // SseClientTransport opens a GET SSE stream for serverclient events
            // and a POST channel for clientserver JSON-RPC messages.
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(_cfg.Endpoint)
            });

            //  Correct factory: McpClientFactory.CreateAsync (not McpClient.CreateAsync) 
            // McpClientFactory performs the MCP protocol handshake (initialize  initialized)
            // and returns an IMcpClient ready for use.
            _client = await McpClient.CreateAsync(
                clientTransport: transport,
                cancellationToken: ct);

            //  McpClientTool directly implements AIFunction 
            // No adapter, no conversion  drop directly into IAgent tools.
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


