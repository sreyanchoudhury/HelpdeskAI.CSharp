using Azure.Storage.Blobs;
using HelpdeskAI.AgentHost.Agents;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HelpdeskAI.AgentHost.Endpoints;

/// <summary>
/// REST endpoints for the Evaluations dashboard page.
/// All routes require the same <c>X-Eval-Key</c> header as the eval invocation endpoint.
///
/// Routes:
///   GET  /agent/eval/results               — list latest execution run summaries
///   GET  /agent/eval/results/{execution}   — scenario details for one execution
///   POST /agent/eval/run                   — trigger a new background eval run
/// </summary>
internal static class EvalResultsEndpoints
{
    internal static IEndpointRouteBuilder MapEvalResultsEndpoints(
        this IEndpointRouteBuilder app, string apiKey, EvalRunnerService runner)
    {
        // ── GET /agent/eval/results ───────────────────────────────────────────
        app.MapGet("/agent/eval/results", async (
            HttpContext ctx,
            IOptions<AzureBlobStorageSettings> blobSettings,
            CancellationToken ct) =>
        {
            if (!ValidateKey(ctx, apiKey)) return Results.Unauthorized();

            var container = new BlobContainerClient(
                blobSettings.Value.ConnectionString, EvalRunnerService.EvalContainerName);

            if (!await container.ExistsAsync(ct))
                return Results.Ok(Array.Empty<object>());

            // Read all blobs and aggregate by execution name.
            var allResults = new List<ScenarioResultDto>();
            await foreach (var blobItem in container.GetBlobsAsync(cancellationToken: ct))
            {
                try
                {
                    var dl = await container.GetBlobClient(blobItem.Name).DownloadContentAsync(ct);
                    var dto = JsonSerializer.Deserialize(dl.Value.Content, EvalJsonContext.Default.ScenarioResultDto);
                    if (dto is not null) allResults.Add(dto);
                }
                catch { /* skip malformed blobs */ }
            }

            var summaries = allResults
                .GroupBy(r => r.ExecutionName)
                .OrderByDescending(g => g.Key) // ISO-sortable name keeps newest first
                .Select(g => new
                {
                    executionName = g.Key,
                    total         = g.Count(),
                    passed        = g.Count(r => r.Passed),
                    failed        = g.Count(r => !r.Passed),
                    isComplete    = g.Count() >= EvalRunnerService.Scenarios.Length,
                    isRunning     = runner.RunningExecution == g.Key,
                    completedAt   = g.Max(r => (DateTime?)r.CreatedAt),
                })
                .ToArray();

            return Results.Ok(summaries);
        })
        .WithName("GetEvalResults")
        .WithDescription("List latest evaluation run summaries. Requires X-Eval-Key header.");

        // ── GET /agent/eval/results/{executionName} ───────────────────────────
        app.MapGet("/agent/eval/results/{executionName}", async (
            HttpContext ctx,
            string executionName,
            IOptions<AzureBlobStorageSettings> blobSettings,
            CancellationToken ct) =>
        {
            if (!ValidateKey(ctx, apiKey)) return Results.Unauthorized();

            var container = new BlobContainerClient(
                blobSettings.Value.ConnectionString, EvalRunnerService.EvalContainerName);

            if (!await container.ExistsAsync(ct)) return Results.NotFound();

            var results = new List<ScenarioResultDto>();
            await foreach (var blobItem in container.GetBlobsAsync(
                Azure.Storage.Blobs.Models.BlobTraits.None,
                Azure.Storage.Blobs.Models.BlobStates.None,
                prefix: executionName + "/",
                cancellationToken: ct))
            {
                try
                {
                    var dl = await container.GetBlobClient(blobItem.Name).DownloadContentAsync(ct);
                    var dto = JsonSerializer.Deserialize(dl.Value.Content, EvalJsonContext.Default.ScenarioResultDto);
                    if (dto is not null) results.Add(dto);
                }
                catch { /* skip */ }
            }

            return results.Count == 0
                ? Results.NotFound()
                : Results.Ok(results.OrderBy(r => r.ScenarioName));
        })
        .WithName("GetEvalExecutionResults")
        .WithDescription("Scenario-level results for one execution. Requires X-Eval-Key header.");

        // ── POST /agent/eval/run ──────────────────────────────────────────────
        app.MapPost("/agent/eval/run", async (
            HttpContext ctx,
            CancellationToken ct) =>
        {
            if (!ValidateKey(ctx, apiKey)) return Results.Unauthorized();

            var executionName = await runner.StartRunAsync(ct);
            return Results.Accepted(value: new
            {
                executionName,
                message = "Eval run started. Poll GET /agent/eval/results for progress.",
            });
        })
        .WithName("TriggerEvalRun")
        .WithDescription("Starts a new background eval run of all 20 golden scenarios (15 v1 + 5 v2). Requires X-Eval-Key header.");

        return app;
    }

    private static bool ValidateKey(HttpContext ctx, string apiKey) =>
        ctx.Request.Headers.TryGetValue(EvalEndpoints.EvalKeyHeader, out var sent) && sent == apiKey;
}
