using Azure;
using Azure.AI.DocumentIntelligence;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.Options;

namespace HelpdeskAI.AgentHost.Infrastructure;

/// <summary>
/// Extracts text from PDF, DOCX, PNG, JPG, and JPEG files using the
/// Azure Document Intelligence prebuilt-read model.
/// </summary>
public sealed class DocumentIntelligenceService : IDocumentIntelligenceService
{
    private readonly DocumentIntelligenceClient _client;
    private readonly ILogger<DocumentIntelligenceService> _logger;

    public DocumentIntelligenceService(
        IOptions<DocumentIntelligenceSettings> settings,
        ILogger<DocumentIntelligenceService> logger)
    {
        var s = settings.Value;
        _client = new DocumentIntelligenceClient(
            new Uri(s.Endpoint),
            new AzureKeyCredential(s.Key));
        _logger = logger;
    }

    public async Task<string> ExtractTextAsync(
        string fileName,
        Stream content,
        string contentType,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Extracting text from '{FileName}' ({ContentType}) via Document Intelligence prebuilt-read",
            fileName, contentType);

        var binaryData = await BinaryData.FromStreamAsync(content, ct);
        var options = new AnalyzeDocumentOptions("prebuilt-read", binaryData);

        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            options,
            ct);

        var text = operation.Value.Content;

        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("Document Intelligence returned no text from '{FileName}'", fileName);
            return $"[No text could be extracted from {fileName}]";
        }

        _logger.LogInformation(
            "Extracted {Length} characters from '{FileName}'", text.Length, fileName);

        return text;
    }
}
