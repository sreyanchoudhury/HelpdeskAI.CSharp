using System.Collections;
using System.Runtime.CompilerServices;
using HelpdeskAI.AgentHost.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelpdeskAI.AgentHost.Infrastructure;

internal static class WorkflowAgentWrapperFactory
{
    public static AIAgent Create(
        AIAgent rawWorkflowAgent,
        IServiceProvider services,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("HelpdeskAI.AgentHost.ToolCapturingMiddleware");

        return new AIAgentBuilder(rawWorkflowAgent)
            .Use(
                (messages, session, options, innerAgent, ct) => RunAsync(messages, session, options, innerAgent, logger, ct),
                (messages, session, options, innerAgent, ct) => RunStreamingAsync(messages, session, options, innerAgent, logger, ct))
            .Build(services);
    }

    private static void CaptureFrontendTools(AgentRunOptions? options, ILogger logger)
    {
        if (options is ChatClientAgentRunOptions chatRunOptions
            && chatRunOptions.ChatOptions?.Tools is { Count: > 0 } tools)
        {
            var frontendTools = FrontendToolForwardingProvider.CaptureFrontendTools(tools);
            logger.LogInformation(
                "[ToolCapture] Captured {FrontendCount}/{TotalCount} CopilotKit frontend tools from AgentRunOptions",
                frontendTools.Count,
                tools.Count);
            return;
        }

        logger.LogWarning(
            "[ToolCapture] No tools in AgentRunOptions (options type: {OptionsType})",
            options?.GetType().Name ?? "null");
    }

    private static async Task<AgentResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        ILogger logger,
        CancellationToken ct)
    {
        CaptureFrontendTools(options, logger);

        try
        {
            var response = await innerAgent.RunAsync(messages, session, options, ct);
            if (TryGetWorkflowContentSafetyException(response, out var workflowContentSafetyException)
                && workflowContentSafetyException is not null)
            {
                logger.LogWarning(
                    workflowContentSafetyException,
                    "[ToolCapture] Converting workflow ErrorContent into a content safety failure.");
                throw workflowContentSafetyException;
            }

            return response;
        }
        catch (ContentSafetyException ex)
        {
            logger.LogWarning(ex, "[ToolCapture] Propagating content safety failure from v2 workflow.");
            throw;
        }
    }

    private static async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken ct)
    {
        CaptureFrontendTools(options, logger);

        var updates = innerAgent.RunStreamingAsync(messages, session, options, ct);
        await using var enumerator = updates.GetAsyncEnumerator(ct);

        while (true)
        {
            AgentResponseUpdate? current = null;
            ContentSafetyException? contentSafetyException = null;

            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;

                current = enumerator.Current;
            }
            catch (ContentSafetyException ex)
            {
                contentSafetyException = ex;
            }

            if (contentSafetyException is not null)
            {
                logger.LogWarning(contentSafetyException, "[ToolCapture] Propagating content safety failure from v2 workflow.");
                throw contentSafetyException;
            }

            if (TryGetWorkflowContentSafetyException(current, out var workflowContentSafetyException)
                && workflowContentSafetyException is not null)
            {
                logger.LogWarning(
                    workflowContentSafetyException,
                    "[ToolCapture] Converting workflow ErrorContent into a content safety failure.");
                throw workflowContentSafetyException;
            }

            yield return current!;
        }
    }

    private static bool TryGetWorkflowContentSafetyException(
        object? responseEnvelope,
        out ContentSafetyException? exception)
    {
        foreach (var errorContent in EnumerateWorkflowErrorContents(responseEnvelope))
        {
            var summary = string.Join(
                "\n",
                new[] { errorContent.Message, errorContent.ErrorCode, errorContent.Details }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

            if (summary.Contains("blocked by Azure content safety", StringComparison.OrdinalIgnoreCase) ||
                summary.Contains("content_filter", StringComparison.OrdinalIgnoreCase))
            {
                exception = new ContentSafetyException(
                    ContentSafetyGuardChatClient.UserFacingMessage,
                    new InvalidOperationException(summary));
                return true;
            }
        }

        exception = null;
        return false;
    }

    private static IEnumerable<ErrorContent> EnumerateWorkflowErrorContents(object? responseEnvelope)
    {
        if (responseEnvelope is null)
            yield break;

        foreach (var content in EnumerateWorkflowContents(responseEnvelope))
        {
            if (content is ErrorContent errorContent)
                yield return errorContent;
        }
    }

    private static IEnumerable<AIContent> EnumerateWorkflowContents(object responseEnvelope)
    {
        if (TryGetPropertyValue(responseEnvelope, "Contents", out var contentsValue))
        {
            foreach (var content in EnumerateAiContents(contentsValue))
                yield return content;

            yield break;
        }

        if (!TryGetPropertyValue(responseEnvelope, "Messages", out var messagesValue) ||
            messagesValue is not IEnumerable messages)
        {
            yield break;
        }

        foreach (var message in messages)
        {
            if (message is not ChatMessage chatMessage)
                continue;

            foreach (var content in chatMessage.Contents)
                yield return content;
        }
    }

    private static IEnumerable<AIContent> EnumerateAiContents(object? contentsValue)
    {
        if (contentsValue is IEnumerable<AIContent> typedContents)
        {
            foreach (var content in typedContents)
                yield return content;

            yield break;
        }

        if (contentsValue is not IEnumerable contents)
            yield break;

        foreach (var content in contents)
        {
            if (content is AIContent aiContent)
                yield return aiContent;
        }
    }

    private static bool TryGetPropertyValue(object instance, string propertyName, out object? value)
    {
        var property = instance.GetType().GetProperty(propertyName);
        if (property is null)
        {
            value = null;
            return false;
        }

        value = property.GetValue(instance);
        return true;
    }
}