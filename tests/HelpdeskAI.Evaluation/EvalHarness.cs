using System.Net.Http.Json;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
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
    /// Results are written to disk (EVAL_REPORT_PATH / %LOCALAPPDATA%\HelpdeskAI\EvalResults).
    /// Response caching is ON — re-runs are fast (no LLM calls) unless scenario input changes.
    /// </summary>
    internal static ReportingConfiguration BuildReportingConfig() =>
        DiskBasedReportingConfiguration.Create(
            storageRootPath: ReportStoragePath,
            evaluators: AllEvaluators,
            chatConfiguration: BuildEvaluatorChatConfig(),
            enableResponseCaching: true);

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
        using var resp = await http.PostAsJsonAsync("/agent/eval", payload, ct);
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
