using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HelpdeskAI.Evaluation;

/// <summary>
/// 12 golden integration test cases for HelpdeskAI agent quality evaluation.
///
/// Prerequisites:
///   1. AgentHost + McpServer are running (locally or pointed at Azure via EVAL_AGENT_URL).
///   2. EVAL_OPENAI_API_KEY is set (same key as appsettings.Development.json AzureOpenAI:ApiKey).
///
/// Run:
///   dotnet test tests/HelpdeskAI.Evaluation
///
/// Report (after installing the dotnet tool):
///   dotnet tool install -g Microsoft.Extensions.AI.Evaluation.Console
///   dotnet aieval report --path "%LOCALAPPDATA%\HelpdeskAI\EvalResults" --output docs/eval-report.html
///
/// Pass/fail gate:
///   Each test asserts the PRIMARY evaluator's metric is Good or Exceptional.
///   All configured evaluators run and are recorded; only the primary is asserted here.
///
/// Notes:
///   - Tests 5–10 are self-contained multi-step requests (create + act) so no pre-seeded data needed.
///   - First run calls the live LLM; subsequent runs use the 14-day disk response cache.
/// </summary>
[TestClass]
public sealed class GoldenTests
{
    private static HttpClient             _http      = null!;
    private static ReportingConfiguration _reporting = null!;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        EvalHarness.EnsureConfigured();
        _http      = new HttpClient
        {
            BaseAddress = new Uri(EvalHarness.AgentBaseUrl),
            Timeout     = TimeSpan.FromMinutes(4),   // tool calls can be slow
        };
        _reporting = EvalHarness.BuildReportingConfig();
    }

    [ClassCleanup]
    public static void Cleanup() => _http?.Dispose();

    // ── Shared runner ─────────────────────────────────────────────────────────

    private static async Task RunScenarioAsync(
        string testName,
        string userMessage,
        string primaryEvaluator,
        CancellationToken ct = default)
    {
        // Call the agent via /agent/eval (synchronous JSON endpoint).
        var agentText = await EvalHarness.CallEvalAsync(_http, userMessage, ct);

        // CreateScenarioRunAsync handles caching + result persistence to disk.
        await using var scenario = await _reporting.CreateScenarioRunAsync(testName, cancellationToken: ct);

        // The string-string overload passes request/response as plain text to the evaluators.
        var result = await scenario.EvaluateAsync(userMessage, agentText, additionalContext: null, cancellationToken: ct);

        EvalHarness.AssertPassed(result, primaryEvaluator);
    }

    // ── 01 — Intent resolution: VPN not working ──────────────────────────────

    [TestMethod]
    [Description("Agent resolves VPN issue intent — checks status or provides troubleshooting.")]
    public async Task Test01_VpnNotWorking_IntentResolution() =>
        await RunScenarioAsync(
            nameof(Test01_VpnNotWorking_IntentResolution),
            "My VPN isn't working.",
            nameof(IntentResolutionEvaluator));

    // ── 02 — Task adherence: create ticket ───────────────────────────────────

    [TestMethod]
    [Description("Agent creates a ticket for a flickering screen and confirms the ticket ID.")]
    public async Task Test02_CreateTicket_TaskAdherence() =>
        await RunScenarioAsync(
            nameof(Test02_CreateTicket_TaskAdherence),
            "Please create a support ticket for my laptop screen which is flickering badly.",
            nameof(TaskAdherenceEvaluator));

    // ── 03 — Task adherence: search tickets ─────────────────────────────────

    [TestMethod]
    [Description("Agent searches and lists open tickets for the current user.")]
    public async Task Test03_SearchTickets_TaskAdherence() =>
        await RunScenarioAsync(
            nameof(Test03_SearchTickets_TaskAdherence),
            "Show me all my open tickets.",
            nameof(TaskAdherenceEvaluator));

    // ── 04 — Relevance: VPN setup KB lookup ─────────────────────────────────

    [TestMethod]
    [Description("Agent answers a KB question about VPN setup — response is relevant to the query.")]
    public async Task Test04_VpnSetupKbLookup_Relevance() =>
        await RunScenarioAsync(
            nameof(Test04_VpnSetupKbLookup_Relevance),
            "How do I set up VPN on Windows? Step-by-step please.",
            nameof(RelevanceEvaluator));

    // ── 05 — Task adherence: create + resolve ticket in one turn ─────────────

    [TestMethod]
    [Description("Agent creates a ticket then marks it resolved in one multi-step turn.")]
    public async Task Test05_CreateAndResolveTicket_TaskAdherence() =>
        await RunScenarioAsync(
            nameof(Test05_CreateAndResolveTicket_TaskAdherence),
            "Create a ticket for 'printer not working on Floor 3' and then mark it as resolved.",
            nameof(TaskAdherenceEvaluator));

    // ── 06 — Task adherence: create + add comment in one turn ────────────────

    [TestMethod]
    [Description("Agent creates a ticket then adds a comment in one turn.")]
    public async Task Test06_CreateAndCommentTicket_TaskAdherence() =>
        await RunScenarioAsync(
            nameof(Test06_CreateAndCommentTicket_TaskAdherence),
            "Create a ticket for 'email not syncing' and add a note: " +
            "'User tried restart, issue persists, escalated to Tier 2'.",
            nameof(TaskAdherenceEvaluator));

    // ── 07 — Task adherence: high-priority ticket with assignment ─────────────

    [TestMethod]
    [Description("Agent creates a High-priority Network ticket and assigns it to the helpdesk team.")]
    public async Task Test07_CreateHighPriorityNetworkTicket_TaskAdherence() =>
        await RunScenarioAsync(
            nameof(Test07_CreateHighPriorityNetworkTicket_TaskAdherence),
            "Create a High priority ticket for a network outage affecting the whole office. " +
            "Assign it to helpdesk-tier2@contoso.com.",
            nameof(TaskAdherenceEvaluator));

    // ── 08 — Intent resolution: team impact query ────────────────────────────

    [TestMethod]
    [Description("Agent correctly resolves intent to check active incidents for the Engineering team.")]
    public async Task Test08_TeamImpactQuery_IntentResolution() =>
        await RunScenarioAsync(
            nameof(Test08_TeamImpactQuery_IntentResolution),
            "What IT issues are currently affecting the Engineering team?",
            nameof(IntentResolutionEvaluator));

    // ── 09 — Task adherence: index KB article ────────────────────────────────

    [TestMethod]
    [Description("Agent indexes a provided text block as a KB article without prompting for permission.")]
    public async Task Test09_IndexKbArticle_TaskAdherence() =>
        await RunScenarioAsync(
            nameof(Test09_IndexKbArticle_TaskAdherence),
            "Please index the following as a KB article titled 'VPN Quick Fix': " +
            "To fix VPN on Windows: 1. Open Network settings. " +
            "2. Disconnect and reconnect VPN. " +
            "3. If that fails, restart the Cisco AnyConnect service. Category: Networking.",
            nameof(TaskAdherenceEvaluator));

    // ── 10 — Task adherence: create → search → close ticket ──────────────────

    [TestMethod]
    [Description("Agent completes a three-step workflow: create, search, then close the ticket.")]
    public async Task Test10_CreateSearchCloseTicket_TaskAdherence() =>
        await RunScenarioAsync(
            nameof(Test10_CreateSearchCloseTicket_TaskAdherence),
            "1. Create a ticket for 'keyboard keys sticking on my laptop'. " +
            "2. Search my open tickets to confirm it's there. " +
            "3. Close it as self-resolved.",
            nameof(TaskAdherenceEvaluator));

    // ── 11 — Intent resolution: possible virus concern ───────────────────────

    [TestMethod]
    [Description("Agent resolves a security concern intent and provides appropriate guidance.")]
    public async Task Test11_PossibleVirus_IntentResolution() =>
        await RunScenarioAsync(
            nameof(Test11_PossibleVirus_IntentResolution),
            "I think my laptop might have a virus. It's been running really slowly and showing weird pop-ups.",
            nameof(IntentResolutionEvaluator));

    // ── 12 — Relevance: unknown KB topic (no hallucination) ──────────────────

    [TestMethod]
    [Description("Agent responds relevantly to an unknown-topic query without hallucinating KB content.")]
    public async Task Test12_UnknownKbTopic_Relevance() =>
        await RunScenarioAsync(
            nameof(Test12_UnknownKbTopic_Relevance),
            "What is the current workaround for the Microsoft Teams issue affecting the APAC region?",
            nameof(RelevanceEvaluator));
}
