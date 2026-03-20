using System.Text.Json;
using Microsoft.Azure.Cosmos;

namespace HelpdeskAI.McpServer.Infrastructure;

/// <summary>
/// Plugs System.Text.Json (with camelCase + string-enum converters) into the Cosmos SDK.
/// Property names in documents match Patch paths: /id, /status, /comments/-, etc.
/// </summary>
internal sealed class CosmosStjSerializer : CosmosSerializer
{
    private readonly JsonSerializerOptions _options;

    internal CosmosStjSerializer(JsonSerializerOptions options) => _options = options;

    public override T FromStream<T>(Stream stream)
    {
        using (stream)
        {
            if (stream.CanSeek && stream.Length == 0) return default!;
            return JsonSerializer.Deserialize<T>(stream, _options)!;
        }
    }

    public override Stream ToStream<T>(T input)
    {
        var ms = new MemoryStream();
        JsonSerializer.Serialize(ms, input, _options);
        ms.Position = 0;
        return ms;
    }
}
