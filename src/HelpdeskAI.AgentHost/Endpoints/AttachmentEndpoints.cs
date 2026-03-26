using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;

namespace HelpdeskAI.AgentHost.Endpoints;

internal static class AttachmentEndpoints
{
    internal static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/attachments
        // Accepts: multipart/form-data with field 'file' + required header 'X-Session-Id'
        // Supported: .txt (StreamReader), .pdf / .docx (Document Intelligence OCR), .png / .jpg / .jpeg (vision)
        // Returns: { fileName, contentType, blobUrl, processedAt }
        app.MapPost("/api/attachments", HandleUploadAsync).RequireAuthorization();

        // GET /api/attachments/{*blobName}
        // Authenticated proxy — streams the blob via Managed Identity (DefaultAzureCredential).
        // The container is PublicAccessType.None so unsigned URIs would return 403.
        app.MapGet("/api/attachments/{*blobName}", HandleDownloadAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleDownloadAsync(
        string blobName,
        IBlobStorageService blobService,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("HelpdeskAI.AgentHost.Endpoints.AttachmentEndpoints");
        try
        {
            var download = await blobService.DownloadAsync(blobName, ct);
            logger.LogInformation("Serving attachment blob '{BlobName}' ({ContentType})", blobName, download.ContentType);
            return Results.Stream(download.Content, download.ContentType,
                fileDownloadName: Path.GetFileName(blobName));
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            return Results.NotFound(new { error = $"Blob '{blobName}' not found." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to stream blob '{BlobName}'", blobName);
            return Results.Problem("Failed to retrieve the attachment.", statusCode: 500);
        }
    }

    private static async Task<IResult> HandleUploadAsync(
        HttpRequest request,
        IBlobStorageService blobService,
        IAttachmentStore attachmentStore,
        IDocumentIntelligenceService docIntelService,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("HelpdeskAI.AgentHost.Endpoints.AttachmentEndpoints");
        if (!request.HasFormContentType)
            return Results.BadRequest(new { error = "Expected multipart/form-data" });

        IFormFile? file;
        try
        {
            var form = await request.ReadFormAsync(ct);
            file = form.Files.GetFile("file");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse multipart form");
            return Results.BadRequest(new { error = "Could not parse the uploaded form." });
        }

        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "No file provided" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var isTxt = ext == ".txt" || file.ContentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase);
        var isDocIntel = ext is ".pdf" or ".docx";
        var isImage = ext is ".png" or ".jpg" or ".jpeg";

        if (!isTxt && !isDocIntel && !isImage)
            return Results.BadRequest(new { error = $"Unsupported file type '{ext}'. Supported: .txt, .pdf, .docx, .png, .jpg, .jpeg" });

        if (!request.Headers.TryGetValue("X-Session-Id", out var sid) || string.IsNullOrWhiteSpace(sid))
            return Results.BadRequest(new { error = "X-Session-Id header is required." });
        var sessionId = sid.ToString();
        logger.LogInformation("[Upload] Received '{FileName}' ({Ext}) for session '{SessionId}'",
            file.FileName, ext, sessionId);

        string? extractedText = null;
        string? imageBase64 = null;
        string? blobUrl = null;
        var kind = AttachmentKind.Text;

        // ── Process content ───────────────────────────────────────────────────
        try
        {
            if (isTxt)
            {
                using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
                extractedText = await reader.ReadToEndAsync(ct);
            }
            else if (isDocIntel)
            {
                extractedText = await docIntelService.ExtractTextAsync(
                    file.FileName, file.OpenReadStream(), file.ContentType, ct);
            }
            else // isImage — read raw bytes for vision content-part injection
            {
                using var ms = new MemoryStream();
                await file.OpenReadStream().CopyToAsync(ms, ct);
                imageBase64 = Convert.ToBase64String(ms.ToArray());
                kind = AttachmentKind.Image;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process attachment '{FileName}'", file.FileName);
            return Results.Problem("Failed to read the uploaded file.", statusCode: 500);
        }

        // ── Upload to Blob — best-effort ──────────────────────────────────────
        // UploadAsync now returns the blob name (not the unsigned Azure URI).
        // Build an authenticated proxy URL so "View original" links don't 403.
        try
        {
            using var uploadStream = file.OpenReadStream();
            var blobName = await blobService.UploadAsync(file.FileName, uploadStream, file.ContentType, ct);
            blobUrl = $"{request.Scheme}://{request.Host}/api/attachments/{blobName}";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Blob upload failed for '{FileName}'; attachment will still be injected into context", file.FileName);
        }

        var attachment = new ProcessedAttachment
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            Kind = kind,
            ExtractedText = extractedText,
            ImageBase64 = imageBase64,
            BlobUrl = blobUrl,
            ProcessedAt = DateTimeOffset.UtcNow
        };

        await attachmentStore.SaveAsync(sessionId, [attachment], ct);

        logger.LogInformation("Attachment '{FileName}' staged for session '{SessionId}'", file.FileName, sessionId);

        return Results.Ok(new
        {
            fileName = attachment.FileName,
            contentType = attachment.ContentType,
            blobUrl = attachment.BlobUrl,
            processedAt = attachment.ProcessedAt
        });
    }
}
