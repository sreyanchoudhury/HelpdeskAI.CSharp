using Azure.Storage.Blobs;
using HelpdeskAI.AgentHost.Abstractions;
using HelpdeskAI.AgentHost.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.OpenAI;

namespace HelpdeskAI.AgentHost.Agents;

/// <summary>
/// Runs 15 eval scenarios against the live agent pipeline and persists per-scenario
/// JSON results to Azure Blob Storage (container: <c>eval-results</c>).
///
/// Single-turn scenarios exercise one user message → agent response.
/// Multi-turn scenarios build a real conversation history by running each intermediate
/// turn through the agent before evaluating only the final exchange.
///
/// Triggered from the frontend Evaluations dashboard via POST /agent/eval/run.
/// Only one run may be active at a time.
/// </summary>
internal sealed class EvalRunnerService(
    IChatClient agentClient,
    IMcpToolsProvider toolsProvider,
    IOptions<AzureOpenAiSettings> aiSettings,
    IOptions<AzureBlobStorageSettings> blobSettings,
    ILogger<EvalRunnerService> log)
{
    internal const string EvalContainerName = "eval-results";

    // Synthetic eval persona — gives ticket/search tools a userId to work with.
    private const string EvalUserCtx =
        "\n\nFor this evaluation session you are assisting: " +
        "eval-user@contoso.com (Eval User, Engineering team). " +
        "Use this identity whenever a tool requires a user email or userId.";

    /// <summary>
    /// Supports both single-turn (UserTurns.Length == 1) and multi-turn scenarios.
    /// For multi-turn, each turn except the last is executed to build conversation history;
    /// only the final exchange is evaluated.
    /// </summary>
    internal record ScenarioSpec(string Name, string[] UserTurns, string PrimaryEvaluator);

    internal static readonly ScenarioSpec[] Scenarios =
    [
        // ── Single-turn: intent resolution ────────────────────────────────────
        new("Test01_VpnNotWorking",
            new[] { "My VPN isn't working." },
            "IntentResolution"),

        new("Test08_TeamImpactQuery",
            new[] { "What IT issues are currently affecting the Engineering team?" },
            "IntentResolution"),

        new("Test11_PossibleVirus",
            new[] { "I think my laptop might have a virus. It's been running really slowly and showing weird pop-ups." },
            "IntentResolution"),

        // ── Single-turn: task adherence ────────────────────────────────────────
        new("Test02_CreateTicket",
            new[] { "Please create a support ticket for my laptop screen which is flickering badly." },
            "TaskAdherence"),

        new("Test03_SearchTickets",
            new[] { "Show me all my open tickets." },
            "TaskAdherence"),

        new("Test05_CreateAndResolveTicket",
            new[] { "Create a ticket for 'printer not working on Floor 3' and then mark it as resolved." },
            "TaskAdherence"),

        new("Test06_CreateAndCommentTicket",
            new[] {
                "Create a ticket for 'email not syncing' and add a note: " +
                "'User tried restart, issue persists, escalated to Tier 2'."
            },
            "TaskAdherence"),

        new("Test07_CreateHighPriorityNetworkTicket",
            new[] {
                "Create a High priority ticket for a network outage affecting the whole office. " +
                "Assign it to helpdesk-tier2@contoso.com."
            },
            "TaskAdherence"),

        new("Test09_IndexKbArticle",
            new[] {
                "Please index the following as a KB article titled 'VPN Quick Fix': " +
                "To fix VPN on Windows: 1. Open Network settings. " +
                "2. Disconnect and reconnect VPN. " +
                "3. If that fails, restart the Cisco AnyConnect service. Category: Networking."
            },
            "TaskAdherence"),

        new("Test10_CreateSearchCloseTicket",
            new[] {
                "1. Create a ticket for 'keyboard keys sticking on my laptop'. " +
                "2. Search my open tickets to confirm it's there. " +
                "3. Close it as self-resolved."
            },
            "TaskAdherence"),

        // ── Single-turn: relevance ─────────────────────────────────────────────
        new("Test04_VpnSetupKbLookup",
            new[] { "How do I set up VPN on Windows? Step-by-step please." },
            "Relevance"),

        new("Test12_UnknownKbTopic",
            new[] { "What is the current workaround for the Microsoft Teams issue affecting the APAC region?" },
            "Relevance"),

        // ── Multi-turn ─────────────────────────────────────────────────────────
        new("Test13_MultiTurn_VpnThenTicket",
            new[] {
                "My VPN isn't working, I can't connect to the office network.",
                "I tried restarting and it's still broken. Can you create a support ticket for this issue?"
            },
            "TaskAdherence"),

        new("Test14_MultiTurn_SearchThenClose",
            new[] {
                "Show me all my open tickets.",
                "Please close the most recent one as self-resolved."
            },
            "TaskAdherence"),

        // ── Edge case: out of scope ────────────────────────────────────────────
        new("Test15_OutOfScope",
            new[] { "Can you write me a poem about Monday mornings? I need it for a team email." },
            "Coherence"),
    ];

    private volatile string? _runningExecution;
    internal string? RunningExecution => _runningExecution;

    internal Task<string> StartRunAsync(CancellationToken appStopping)
    {
        if (_runningExecution is { } existing)
        {
            log.LogWarning("Eval run requested but {Execution} is already in progress", existing);
            return Task.FromResult(existing);
        }

        var executionName = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        _ = Task.Run(() => RunAsync(executionName, appStopping), CancellationToken.None);
        return Task.FromResult(executionName);
    }

    private async Task RunAsync(string executionName, CancellationToken ct)
    {
        _runningExecution = executionName;
        try
        {
            var settings  = aiSettings.Value;
            var container = new BlobContainerClient(blobSettings.Value.ConnectionString, EvalContainerName);
            await container.CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.None,
                cancellationToken: ct);

            // Raw evaluator client — no middleware pipeline so evaluator LLM calls
            // don't get wrapped in FunctionInvocation or usage-capture.
            IChatClient evalClient = string.IsNullOrWhiteSpace(settings.ApiKey)
                ? new AzureOpenAIClient(new Uri(settings.Endpoint), new Azure.Identity.DefaultAzureCredential())
                      .GetChatClient(settings.ChatDeployment).AsIChatClient()
                : new AzureOpenAIClient(
                      new Uri(settings.Endpoint),
                      new Azure.AzureKeyCredential(settings.ApiKey))
                      .GetChatClient(settings.ChatDeployment).AsIChatClient();

            var chatConfig = new ChatConfiguration(evalClient);
            IEvaluator composite = new CompositeEvaluator(
                new IntentResolutionEvaluator(),
                new TaskAdherenceEvaluator(),
                new RelevanceEvaluator(),
                new CoherenceEvaluator());

            var tools = (await toolsProvider.GetToolsAsync(ct)).Cast<AITool>().ToList();

            foreach (var scenario in Scenarios)
            {
                if (ct.IsCancellationRequested) break;

                log.LogInformation("Eval {Exec} — starting {Scenario}", executionName, scenario.Name);
                await RunScenarioAsync(executionName, scenario, tools, composite, chatConfig, container, ct);
            }

            log.LogInformation("Eval run {Exec} completed", executionName);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Eval run {Exec} failed unexpectedly", executionName);
        }
        finally
        {
            _runningExecution = null;
        }
    }

    private async Task RunScenarioAsync(
        string executionName,
        ScenarioSpec scenario,
        List<AITool> tools,
        IEvaluator composite,
        ChatConfiguration chatConfig,
        BlobContainerClient container,
        CancellationToken ct)
    {
        ScenarioResultDto result;
        try
        {
            // ── Build conversation history ────────────────────────────────────
            // For multi-turn scenarios, run each intermediate turn through the agent
            // so the final turn is evaluated in realistic conversational context.
            var history = new List<ChatMessage>
            {
                new(ChatRole.System, HelpdeskAgentFactory.BaseInstructions + EvalUserCtx),
            };

            var agentOptions = new ChatOptions { Tools = tools };

            // Execute all turns except the last to build history.
            foreach (var turn in scenario.UserTurns[..^1])
            {
                history.Add(new ChatMessage(ChatRole.User, turn));
                var mid = await agentClient.GetResponseAsync(history, agentOptions, ct);
                history.Add(new ChatMessage(ChatRole.Assistant, mid.Text ?? string.Empty));
                log.LogDebug("Eval {Exec} — {Scenario}: intermediate turn complete", executionName, scenario.Name);
            }

            // Final turn — this is the exchange that gets evaluated.
            var finalMessage = scenario.UserTurns[^1];
            history.Add(new ChatMessage(ChatRole.User, finalMessage));
            var agentResp = await agentClient.GetResponseAsync(history, agentOptions, ct);
            var agentText = agentResp.Text ?? string.Empty;

            // ── Evaluate ─────────────────────────────────────────────────────
            // Evaluate only the final user turn and agent response.
            var evalMessages  = new List<ChatMessage> { new(ChatRole.User, finalMessage) };
            var modelResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, agentText)]);
            var evalResult    = await composite.EvaluateAsync(evalMessages, modelResponse, chatConfig, null, ct);

            // Use the evaluator's own static metric name constant — the actual names
            // have spaces ("Intent Resolution", "Task Adherence") and won't match
            // a simple substring search on the evaluator class name prefix.
            var primaryMetricName = scenario.PrimaryEvaluator switch
            {
                "IntentResolution" => IntentResolutionEvaluator.IntentResolutionMetricName,
                "TaskAdherence"    => TaskAdherenceEvaluator.TaskAdherenceMetricName,
                "Relevance"        => RelevanceEvaluator.RelevanceMetricName,
                "Coherence"        => CoherenceEvaluator.CoherenceMetricName,
                _                  => scenario.PrimaryEvaluator,
            };
            evalResult.Metrics.TryGetValue(primaryMetricName, out var primaryMetric);
            bool passed = primaryMetric?.Interpretation?.Rating is
                EvaluationRating.Good or EvaluationRating.Exceptional;

            result = new ScenarioResultDto(
                ExecutionName:    executionName,
                ScenarioName:     scenario.Name,
                Message:          finalMessage,
                AgentResponse:    agentText,
                PrimaryEvaluator: scenario.PrimaryEvaluator,
                Passed:           passed,
                Metrics: evalResult.Metrics
                    .Select(kv => new MetricResultDto(
                        kv.Key,
                        kv.Value.Interpretation?.Rating.ToString() ?? "Inconclusive",
                        kv.Value.Interpretation?.Reason ?? string.Empty))
                    .ToArray(),
                CreatedAt: DateTime.UtcNow);

            log.LogInformation("Eval {Exec} — {Scenario}: {Result}",
                executionName, scenario.Name, passed ? "PASSED" : "FAILED");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Eval {Exec} — {Scenario}: exception", executionName, scenario.Name);
            result = new ScenarioResultDto(
                executionName, scenario.Name, scenario.UserTurns[^1],
                $"ERROR: {ex.Message}",
                scenario.PrimaryEvaluator, Passed: false, [], DateTime.UtcNow);
        }

        // ── Persist to blob: eval-results/{executionName}/{scenarioName}.json ──
        try
        {
            var json     = JsonSerializer.SerializeToUtf8Bytes(result, EvalJsonContext.Default.ScenarioResultDto);
            var blobName = $"{executionName}/{scenario.Name}.json";
            using var ms = new MemoryStream(json);
            await container.UploadBlobAsync(blobName, ms, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Eval {Exec} — {Scenario}: failed to persist result to blob",
                executionName, scenario.Name);
        }
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

internal record ScenarioResultDto(
    string ExecutionName,
    string ScenarioName,
    string Message,
    string AgentResponse,
    string PrimaryEvaluator,
    bool Passed,
    MetricResultDto[] Metrics,
    DateTime CreatedAt);

internal record MetricResultDto(string Name, string Rating, string Reason);

[JsonSerializable(typeof(ScenarioResultDto))]
[JsonSerializable(typeof(MetricResultDto))]
[JsonSerializable(typeof(MetricResultDto[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
internal partial class EvalJsonContext : JsonSerializerContext { }
