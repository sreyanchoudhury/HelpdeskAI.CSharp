using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Azure.Storage.Files.DataLake;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelpdeskAI.Evaluation;

/// <summary>
/// Shared helpers for the golden eval test suite.
/// Configuration priority: environment variables → defaults (pointing at local dev).
/// </summary>
internal static class EvalHarness
{
    // ── Configuration ────────────────────────────────────────────────────────

    /// <summary>
    /// Base URL of the AgentHost to evaluate.
    /// Override with EVAL_AGENT_URL to target the Azure deployment.
    /// </summary>
    internal static readonly string AgentBaseUrl =
        Environment.GetEnvironmentVariable("EVAL_AGENT_URL") ?? "http://localhost:5200";

    private static readonly string OaiEndpoint =
        Environment.GetEnvironmentVariable("EVAL_OPENAI_ENDPOINT")
        ?? "https://eus-2-oai.openai.azure.com/";

    private static readonly string OaiApiKey =
        Environment.GetEnvironmentVariable("EVAL_OPENAI_API_KEY") ?? string.Empty;

    private static readonly string OaiDeployment =
        Environment.GetEnvironmentVariable("EVAL_OPENAI_DEPLOYMENT") ?? "gpt-4.1-mini";

    private static readonly string ReportStoragePath =
        Environment.GetEnvironmentVariable("EVAL_REPORT_PATH")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HelpdeskAI", "EvalResults");

    /// <summary>
    /// Optional Azure Storage connection string for blob-backed result persistence.
    /// When set, results are written to Azure Blob Storage (ADLS Gen2) so the
    /// frontend Evaluations dashboard can read them.
    /// Set via: EVAL_BLOB_CONNECTION_STRING env var.
    /// Falls back to disk-based storage when absent (preserves local dev behaviour).
    /// </summary>
    private static readonly string? BlobConnectionString =
        Environment.GetEnvironmentVariable("EVAL_BLOB_CONNECTION_STRING");

    private static readonly string BlobContainerName =
        Environment.GetEnvironmentVariable("EVAL_BLOB_CONTAINER") ?? "eval-results";

    /// <summary>
    /// Optional API key sent as <c>X-Eval-Key</c> when targeting a remote AgentHost.
    /// Required when EVAL_AGENT_URL is set (remote); ignored for localhost (no key needed locally).
    /// Set via: EVAL_API_KEY env var = value of Evaluation:ApiKey in the deployed Container App.
    /// </summary>
    private static readonly string? EvalApiKey =
        Environment.GetEnvironmentVariable("EVAL_API_KEY");

    private static readonly bool IsRemote =
        !AgentBaseUrl.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase);

    // ── Pre-flight guard ─────────────────────────────────────────────────────

    /// <summary>
    /// Call at the top of every <c>[ClassInitialize]</c>.
    /// Marks all tests in the class as <see cref="Assert.Inconclusive"/> with clear
    /// setup instructions if any required environment variable is missing, instead of
    /// letting them explode with a cryptic <see cref="Azure.RequestFailedException"/>.
    /// </summary>
    internal static void EnsureConfigured()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(OaiApiKey))             missing.Add("EVAL_OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(OaiEndpoint))           missing.Add("EVAL_OPENAI_ENDPOINT");
        if (IsRemote && string.IsNullOrWhiteSpace(EvalApiKey)) missing.Add("EVAL_API_KEY");

        if (missing.Count > 0)
            Assert.Inconclusive(
                $"Eval tests skipped — set the following environment variables: " +
                $"{string.Join(", ", missing)}. " +
                $"EVAL_OPENAI_API_KEY/ENDPOINT match appsettings.Development.json. " +
                $"EVAL_AGENT_URL targets the deployed AgentHost (default: localhost:5200). " +
                $"EVAL_API_KEY must match Evaluation__ApiKey set in the Container App env vars.");
    }

    // ── Evaluators ───────────────────────────────────────────────────────────

    /// <summary>All evaluators active across every scenario run.</summary>
    internal static IReadOnlyList<IEvaluator> AllEvaluators =>
    [
        new IntentResolutionEvaluator(),
        new TaskAdherenceEvaluator(),
        new RelevanceEvaluator(),
        new CoherenceEvaluator(),
    ];

    // ── Factory helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="ChatConfiguration"/> backed by the same Azure OpenAI deployment
    /// as AgentHost, used by LLM-based evaluators to judge response quality.
    /// Set EVAL_OPENAI_API_KEY to the value from appsettings.Development.json.
    /// </summary>
    internal static ChatConfiguration BuildEvaluatorChatConfig()
    {
        IChatClient evalClient = new AzureOpenAIClient(
                new Uri(OaiEndpoint),
                new AzureKeyCredential(OaiApiKey))
            .GetChatClient(OaiDeployment)
            .AsIChatClient();

        return new ChatConfiguration(evalClient);
    }

    /// <summary>
    /// Creates the <see cref="ReportingConfiguration"/> that drives all scenario runs.
    /// When EVAL_BLOB_CONNECTION_STRING is set, results are persisted to Azure Storage
    /// (ADLS Gen2) so the frontend Evaluations dashboard can read them.
    /// Otherwise, falls back to disk (EVAL_REPORT_PATH / %LOCALAPPDATA%\HelpdeskAI\EvalResults)
    /// with response caching ON for fast local re-runs.
    /// </summary>
    internal static ReportingConfiguration BuildReportingConfig()
    {
        if (!string.IsNullOrWhiteSpace(BlobConnectionString))
        {
            // Azure Storage (ADLS Gen2) — requires hierarchical namespace enabled on the account.
            var serviceClient   = new DataLakeServiceClient(BlobConnectionString);
            var fileSystemClient = serviceClient.GetFileSystemClient(BlobContainerName);
            var dirClient       = fileSystemClient.GetDirectoryClient("/");
            return AzureStorageReportingConfiguration.Create(
                client: dirClient,
                evaluators: AllEvaluators,
                chatConfiguration: BuildEvaluatorChatConfig(),
                enableResponseCaching: false,   // no cache in blob mode — always fresh
                executionName: DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss"));
        }

        // Local disk (default) — fast re-runs via response caching.
        return DiskBasedReportingConfiguration.Create(
            storageRootPath: ReportStoragePath,
            evaluators: AllEvaluators,
            chatConfiguration: BuildEvaluatorChatConfig(),
            enableResponseCaching: true);
    }

    // ── Agent caller ─────────────────────────────────────────────────────────

    /// <summary>
    /// Calls <c>POST /agent/eval</c> and returns the agent's complete text response.
    /// The endpoint runs the full IChatClient pipeline (function invocation, tools, logging)
    /// and returns synchronous JSON — no SSE parsing needed.
    /// </summary>
    internal static async Task<string> CallEvalAsync(
        HttpClient http, string message, CancellationToken ct = default)
    {
        var payload = new { message };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/agent/eval")
        {
            Content = JsonContent.Create(payload)
        };
        if (!string.IsNullOrWhiteSpace(EvalApiKey))
            request.Headers.Add("X-Eval-Key", EvalApiKey);
        using var resp = await http.SendAsync(request, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("response").GetString() ?? string.Empty;
    }

    // ── Assertion helper ─────────────────────────────────────────────────────

    /// <summary>
    /// Asserts that the primary evaluator's metric is Good or Exceptional.
    /// Accepts either the full evaluator class name (e.g. "IntentResolutionEvaluator")
    /// or the bare metric name fragment (e.g. "IntentResolution").
    /// Other evaluators' metrics are still recorded in the report for broader analysis.
    /// </summary>
    internal static void AssertPassed(EvaluationResult result, string evaluatorNameOrClass)
    {
        // Strip "Evaluator" suffix so both "IntentResolutionEvaluator" and "IntentResolution"
        // match the metric name "IntentResolution" that the evaluator produces.
        var fragment = evaluatorNameOrClass.Replace(
            "Evaluator", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

        var metric = result.Metrics.Values.FirstOrDefault(
            m => m.Name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        Assert.IsNotNull(metric,
            $"No metric matching '{fragment}' found. " +
            $"Available: [{string.Join(", ", result.Metrics.Keys)}]");

        Assert.IsTrue(
            metric.Interpretation?.Rating is EvaluationRating.Good or EvaluationRating.Exceptional,
            $"[{metric.Name}] Expected Good or Exceptional but got " +
            $"{metric.Interpretation?.Rating ?? EvaluationRating.Inconclusive}. " +
            $"Reason: {metric.Interpretation?.Reason ?? "(none)"}");
    }
}
