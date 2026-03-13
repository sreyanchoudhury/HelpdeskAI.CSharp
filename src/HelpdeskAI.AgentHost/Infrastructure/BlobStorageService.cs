using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.Options;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal sealed class BlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobStorageService> _log;
    private bool _containerEnsured;

    public BlobStorageService(IOptions<AzureBlobStorageSettings> opts, ILogger<BlobStorageService> log)
    {
        _log = log;
        var settings = opts.Value;
        var serviceClient = new BlobServiceClient(settings.ConnectionString);
        _container = serviceClient.GetBlobContainerClient(settings.ContainerName);
    }

    public async Task<string> UploadAsync(string fileName, Stream content, string contentType, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);

        // GUID prefix avoids name collisions; original name preserved for diagnostics
        var blobName = $"{Guid.NewGuid():N}/{fileName}";
        var blobClient = _container.GetBlobClient(blobName);

        await blobClient.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, ct);

        _log.LogInformation("Uploaded attachment blob: {BlobName}", blobName);
        return blobClient.Uri.ToString();
    }

    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (_containerEnsured) return;
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        _containerEnsured = true;
    }
}
