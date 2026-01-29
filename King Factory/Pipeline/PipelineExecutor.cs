using LittleHelperAI.KingFactory;
using LittleHelperAI.KingFactory.Engine;
using LittleHelperAI.KingFactory.Intent;
using LittleHelperAI.KingFactory.Models;
using LittleHelperAI.KingFactory.Prompts;
using LittleHelperAI.KingFactory.Tools;
using LittleHelperAI.KingFactory.Validation;
using ValidationIssue = LittleHelperAI.KingFactory.Validation.ValidationIssue;
using ValidationSeverity = LittleHelperAI.KingFactory.Validation.ValidationSeverity;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LittleHelperAI.KingFactory.Pipeline;

public interface IPipelineExecutor
{
    IAsyncEnumerable<FactoryOutput> ProcessAsync(
        string conversationId,
        string message,
        CancellationToken cancellationToken = default);
}

public class PipelineExecutor : IPipelineExecutor
{
    private const float PlanningConfidenceThreshold = 0.7f;
    private const float RequirementsTemperature = 0.1f;
    private const int RequirementsMaxTokens = 600;

    private readonly ILogger<PipelineExecutor> _logger;
    private readonly ILogger _traceLogger;
    private readonly IPipelineRegistry _pipelineRegistry;
    private readonly IMessageHandlerFactory _messageHandlerFactory;
    private readonly IValidationPass _validationPass;
    private readonly IIntentClassifier _intentClassifier;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IUnifiedLlmProvider _llmProvider;
    private readonly IRequirementsPrompt _requirementsPrompt;
    private readonly IToolRouter _toolRouter;

    public PipelineExecutor(
        ILogger<PipelineExecutor> logger,
        ILoggerFactory loggerFactory,
        IPipelineRegistry pipelineRegistry,
        IMessageHandlerFactory messageHandlerFactory,
        IValidationPass validationPass,
        IIntentClassifier intentClassifier,
        IPromptBuilder promptBuilder,
        IUnifiedLlmProvider llmProvider,
        IRequirementsPrompt requirementsPrompt,
        IToolRouter toolRouter)
    {
        _logger = logger;
        _traceLogger = loggerFactory.CreateLogger("PipelineTrace");
        _pipelineRegistry = pipelineRegistry;
        _messageHandlerFactory = messageHandlerFactory;
        _validationPass = validationPass;
        _intentClassifier = intentClassifier;
        _promptBuilder = promptBuilder;
        _llmProvider = llmProvider;
        _requirementsPrompt = requirementsPrompt;
        _toolRouter = toolRouter;
    }

    public async IAsyncEnumerable<FactoryOutput> ProcessAsync(
        string conversationId,
        string message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var pipeline = _pipelineRegistry.GetPipelineForMessage(message);
        var steps = pipeline.Steps ?? new List<PipelineStep>();
        _logger.LogInformation("Pipeline selected: {PipelineId} ({PipelineName})", pipeline.Id, pipeline.Name);
        _traceLogger.LogInformation(
            "Pipeline={PipelineId} Steps={Steps} Config={Config}",
            pipeline.Id,
            string.Join(", ", steps.OrderBy(s => s.Order).Select(s => $"{s.Order}:{s.Type}")),
            JsonSerializer.Serialize(pipeline.Config));

        var intent = await _intentClassifier.ClassifyAsync(message, cancellationToken);
        _traceLogger.LogInformation("Intent={Intent} Category={Category} Confidence={Confidence:P0}",
            intent.Intent, intent.Category, intent.Confidence);

        var forceChatMode = pipeline.Id == "bolt-parody";
        var isCodeIntent = !forceChatMode && IsCodeIntent(intent.Intent);
        var isPlanningIntent = !forceChatMode && intent.Intent == IntentType.Planning && intent.Confidence >= PlanningConfidenceThreshold;
        var isToolIntent = !forceChatMode && IsToolIntent(intent.Intent);

        if (isCodeIntent)
        {
            await foreach (var output in ProcessCodePipelineAsync(conversationId, message, pipeline, isToolIntent, cancellationToken))
            {
                yield return output;
            }

            yield break;
        }

        var handlerOptions = BuildBaseOptions(pipeline);
        handlerOptions.PlanningMode = isPlanningIntent;
        handlerOptions.EnableTools = isToolIntent && HasToolSteps(pipeline, steps);
        handlerOptions.InjectConversation = pipeline.Config.InjectConversation && !isPlanningIntent;
        handlerOptions.ForceLastUser = isPlanningIntent || isToolIntent;
        handlerOptions.TaskLock = BuildTaskLock();
        handlerOptions.CodeMode = false;
        handlerOptions.FixMode = false;
        handlerOptions.SystemPromptOverride = pipeline.Config.PromptOverrides?.SystemPrompt;

        if (forceChatMode)
        {
            handlerOptions.PlanningMode = false;
            handlerOptions.InjectConversation = false;
            handlerOptions.ForceLastUser = true;
            handlerOptions.TaskLock = null;
            handlerOptions.CodeMode = false;
            handlerOptions.FixMode = false;
            handlerOptions.SuppressToolDescriptions = true;
            // Enable tools for bolt-parody to allow artifact/tool execution
            handlerOptions.EnableTools = HasToolSteps(pipeline, steps);
        }

        var handler = _messageHandlerFactory.Create(handlerOptions);
        var responseBuilder = new StringBuilder();
        var sawToolCall = false;
        var needsToolRetry = false;

        if (handlerOptions.EnableTools)
        {
            await foreach (var output in handler.ProcessWithToolsStructuredAsync(
                conversationId,
                message,
                handlerOptions,
                cancellationToken))
            {
                switch (output.Type)
                {
                    case MessageOutputType.Token:
                        responseBuilder.Append(output.Content);
                        yield return new FactoryOutput
                        {
                            Type = FactoryOutputType.Token,
                            Content = output.Content,
                            IsPartial = true
                        };
                        break;
                    case MessageOutputType.ToolCall:
                        yield return new FactoryOutput
                        {
                            Type = FactoryOutputType.ToolCall,
                            Content = output.Content,
                            IsPartial = true,
                            Metadata = new Dictionary<string, object>
                            {
                                ["toolName"] = output.ToolName ?? "unknown",
                                ["toolArguments"] = output.ToolArguments ?? new Dictionary<string, object>()
                            }
                        };
                        sawToolCall = true;
                        if (forceChatMode && string.Equals(output.ToolName, "write_file", StringComparison.OrdinalIgnoreCase))
                        {
                            if (output.ToolArguments != null &&
                                output.ToolArguments.TryGetValue("content", out var contentObj))
                            {
                                var content = contentObj?.ToString() ?? "";
                                if (IsPlaceholderContent(content))
                                {
                                    needsToolRetry = true;
                                }
                            }
                        }
                        break;
                    case MessageOutputType.ToolResult:
                        yield return new FactoryOutput
                        {
                            Type = FactoryOutputType.ToolResult,
                            Content = output.Content,
                            IsPartial = true,
                            Metadata = new Dictionary<string, object>
                            {
                                ["toolName"] = output.ToolName ?? "unknown",
                                ["toolSuccess"] = output.ToolSuccess ?? false
                            }
                        };
                        break;
                    case MessageOutputType.Status:
                        yield return new FactoryOutput
                        {
                            Type = FactoryOutputType.Progress,
                            Content = output.Content,
                            IsPartial = true
                        };
                        break;
                    case MessageOutputType.Error:
                        yield return new FactoryOutput
                        {
                            Type = FactoryOutputType.Error,
                            Content = output.Content,
                            IsPartial = true
                        };
                        break;
                }
            }
        }
        else
        {
            await foreach (var token in handler.ProcessAsync(
                conversationId,
                message,
                handlerOptions,
                cancellationToken))
            {
                responseBuilder.Append(token);
                yield return new FactoryOutput
                {
                    Type = FactoryOutputType.Token,
                    Content = token,
                    IsPartial = true
                };
            }
        }

        // Only retry if no tool calls AND no bolt artifacts (artifacts can be parsed directly)
        if (forceChatMode && handlerOptions.EnableTools && !ContainsToolCall(responseBuilder.ToString()) && !ContainsBoltArtifact(responseBuilder.ToString()))
        {
            yield return new FactoryOutput
            {
                Type = FactoryOutputType.Progress,
                Content = "Retrying with strict tool-call enforcement...",
                IsPartial = true
            };

            responseBuilder.Clear();
            var retryOptions = BuildBaseOptions(pipeline);
            retryOptions.EnableTools = true;
            retryOptions.InjectConversation = false;
            retryOptions.ForceLastUser = true;
            retryOptions.PlanningMode = false;
            retryOptions.TaskLock = null;
            retryOptions.CodeMode = false;
            retryOptions.FixMode = false;
            retryOptions.SystemPromptOverride = pipeline.Config.PromptOverrides?.SystemPrompt;
            retryOptions.DeveloperPrompt = AppendToolEnforcement(pipeline.Config.PromptOverrides?.DeveloperPrompt);
            retryOptions.SuppressToolDescriptions = true;

            await foreach (var output in handler.ProcessWithToolsStructuredAsync(
                conversationId,
                message,
                retryOptions,
                cancellationToken))
            {
                switch (output.Type)
                {
                    case MessageOutputType.Token:
                        responseBuilder.Append(output.Content);
                        yield return new FactoryOutput
                        {
                            Type = FactoryOutputType.Token,
                            Content = output.Content,
                            IsPartial = true
                        };
                        break;
                    case MessageOutputType.ToolCall:
                        yield return new FactoryOutput
                        {
                            Type = FactoryOutputType.ToolCall,
                            Content = output.Content,
                            IsPartial = true,
                            Metadata = new Dictionary<string, object>
                            {
                                ["toolName"] = output.ToolName ?? "unknown",
                                ["toolArguments"] = output.ToolArguments ?? new Dictionary<string, object>()
                            }
                        };
                        break;
                    case MessageOutputType.ToolResult:
                        yield return new FactoryOutput
                        {
                            Type = FactoryOutputType.ToolResult,
                            Content = output.Content,
                            IsPartial = true,
                            Metadata = new Dictionary<string, object>
                            {
                                ["toolName"] = output.ToolName ?? "unknown",
                                ["toolSuccess"] = output.ToolSuccess ?? false
                            }
                        };
                        break;
                }
            }
        }
        else if (forceChatMode && handlerOptions.EnableTools && sawToolCall && needsToolRetry)
        {
            yield return new FactoryOutput
            {
                Type = FactoryOutputType.Progress,
                Content = "Retrying with non-placeholder file content...",
                IsPartial = true
            };

            responseBuilder.Clear();
            var retryOptions = BuildBaseOptions(pipeline);
            retryOptions.EnableTools = true;
            retryOptions.InjectConversation = false;
            retryOptions.ForceLastUser = true;
            retryOptions.PlanningMode = false;
            retryOptions.TaskLock = null;
            retryOptions.CodeMode = false;
            retryOptions.FixMode = false;
            retryOptions.SystemPromptOverride = pipeline.Config.PromptOverrides?.SystemPrompt;
            retryOptions.DeveloperPrompt = AppendToolEnforcement(pipeline.Config.PromptOverrides?.DeveloperPrompt);
            retryOptions.SuppressToolDescriptions = true;

            await foreach (var output in handler.ProcessWithToolsStructuredAsync(
                conversationId,
                message,
                retryOptions,
                cancellationToken))
            {
                switch (output.Type)
                {
                    case MessageOutputType.Token:
                        responseBuilder.Append(output.Content);
                        yield return new FactoryOutput
                        {
                            Type = FactoryOutputType.Token,
                            Content = output.Content,
                            IsPartial = true
                        };
                        break;
                    case MessageOutputType.ToolCall:
                        yield return new FactoryOutput
                        {
                            Type = FactoryOutputType.ToolCall,
                            Content = output.Content,
                            IsPartial = true,
                            Metadata = new Dictionary<string, object>
                            {
                                ["toolName"] = output.ToolName ?? "unknown",
                                ["toolArguments"] = output.ToolArguments ?? new Dictionary<string, object>()
                            }
                        };
                        break;
                    case MessageOutputType.ToolResult:
                        yield return new FactoryOutput
                        {
                            Type = FactoryOutputType.ToolResult,
                            Content = output.Content,
                            IsPartial = true,
                            Metadata = new Dictionary<string, object>
                            {
                                ["toolName"] = output.ToolName ?? "unknown",
                                ["toolSuccess"] = output.ToolSuccess ?? false
                            }
                        };
                        break;
                }
            }
        }
        else if (forceChatMode && handlerOptions.EnableTools && ContainsBoltArtifact(responseBuilder.ToString()))
        {
            await foreach (var artifactOutput in ExecuteBoltArtifactsAsync(responseBuilder.ToString(), cancellationToken))
            {
                yield return artifactOutput;
            }
        }
        else if (forceChatMode && handlerOptions.EnableTools && ContainsCodeBlocks(responseBuilder.ToString()))
        {
            // Fallback: LLM output markdown code blocks instead of artifacts - extract and write files
            await foreach (var codeOutput in ExtractAndWriteCodeBlocksAsync(responseBuilder.ToString(), cancellationToken))
            {
                yield return codeOutput;
            }
        }

        yield return new FactoryOutput
        {
            Type = FactoryOutputType.Complete,
            Content = responseBuilder.ToString(),
            IsFinal = true
        };
    }

    private async IAsyncEnumerable<FactoryOutput> ProcessCodePipelineAsync(
        string conversationId,
        string message,
        PipelineDefinition pipeline,
        bool isToolIntent,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var requirements = await ExtractRequirementsAsync(message, cancellationToken);

        if (requirements.Missing.Count > 0)
        {
            var missingQuestion = BuildMissingInfoPrompt(requirements);
            yield return new FactoryOutput
            {
                Type = FactoryOutputType.Token,
                Content = missingQuestion,
                IsPartial = true
            };
            yield return new FactoryOutput
            {
                Type = FactoryOutputType.Complete,
                Content = missingQuestion,
                IsFinal = true
            };
            yield break;
        }

        var handlerOptions = BuildBaseOptions(pipeline);
        handlerOptions.EnableTools = false;
        handlerOptions.InjectConversation = false;
        handlerOptions.PlanningMode = false;
        handlerOptions.ForceLastUser = true;
        handlerOptions.TaskLock = BuildTaskLock();
        handlerOptions.CodeMode = true;
        handlerOptions.FixMode = false;
        handlerOptions.DeveloperPrompt = BuildCodeDeveloperPrompt(requirements);
        handlerOptions.SystemPromptOverride = pipeline.Config.PromptOverrides?.SystemPrompt;

        var handler = _messageHandlerFactory.Create(handlerOptions);
        var responseBuilder = new StringBuilder();

        await foreach (var token in handler.ProcessAsync(
            conversationId,
            message,
            handlerOptions,
            cancellationToken))
        {
            responseBuilder.Append(token);
            yield return new FactoryOutput
            {
                Type = FactoryOutputType.Token,
                Content = token,
                IsPartial = true
            };
        }

        var responseText = responseBuilder.ToString();
        var validation = ValidateCodeOutput(message, responseText, requirements);

        if (!validation.IsValid)
        {
            yield return new FactoryOutput
            {
                Type = FactoryOutputType.Progress,
                Content = "Retrying with fixes...",
                IsPartial = true
            };

            var fixPrompt = BuildFixDeveloperPrompt(requirements, validation, responseText);
            var fixOptions = BuildBaseOptions(pipeline);
            fixOptions.EnableTools = false;
            fixOptions.InjectConversation = false;
            fixOptions.PlanningMode = false;
            fixOptions.ForceLastUser = true;
            fixOptions.TaskLock = BuildTaskLock();
            fixOptions.CodeMode = true;
            fixOptions.FixMode = true;
            fixOptions.DeveloperPrompt = fixPrompt;
            fixOptions.SystemPromptOverride = pipeline.Config.PromptOverrides?.SystemPrompt;

            var fixResponse = await GenerateOnceAsync(message, fixOptions, cancellationToken);
            responseText = fixResponse;
            yield return new FactoryOutput
            {
                Type = FactoryOutputType.Token,
                Content = responseText,
                IsPartial = true
            };
        }

        yield return new FactoryOutput
        {
            Type = FactoryOutputType.Complete,
            Content = responseText,
            IsFinal = true
        };
    }

    private MessageHandlerOptions BuildBaseOptions(PipelineDefinition pipeline)
    {
        return new MessageHandlerOptions
        {
            MaxContextTokens = pipeline.Config.MaxContextTokens,
            MaxToolIterations = pipeline.Config.MaxToolIterations,
            MaxOutputTokens = pipeline.Config.MaxOutputTokens,
            Temperature = pipeline.Config.Temperature,
            PipelineId = pipeline.Id,
            PipelineName = pipeline.Name,
            DeveloperPrompt = pipeline.Config.PromptOverrides?.DeveloperPrompt,
            SystemPromptOverride = pipeline.Config.PromptOverrides?.SystemPrompt
        };
    }

    private async Task<RequirementsExtraction> ExtractRequirementsAsync(string message, CancellationToken cancellationToken)
    {
        var prompt = _promptBuilder.BuildPrompt(
            new List<ChatMessage> { new() { Role = "user", Content = message } },
            new PromptContext
            {
                TaskLock = BuildTaskLock(),
                DeveloperPrompt = _requirementsPrompt.Content,
                PlanningMode = false,
                CodeMode = false,
                FixMode = false
            });

        var response = await _llmProvider.GenerateAsync(
            prompt,
            maxTokens: RequirementsMaxTokens,
            temperature: RequirementsTemperature,
            cancellationToken: cancellationToken);

        _traceLogger.LogInformation("RequirementsExtraction Response={Response}", response);
        return ParseRequirements(message, response);
    }

    private async Task<string> GenerateOnceAsync(string message, MessageHandlerOptions options, CancellationToken cancellationToken)
    {
        var prompt = _promptBuilder.BuildPrompt(
            new List<ChatMessage> { new() { Role = "user", Content = message } },
            new PromptContext
            {
                TaskLock = options.TaskLock,
                DeveloperPrompt = options.DeveloperPrompt,
                PlanningMode = options.PlanningMode,
                ToolsContext = options.EnableTools ? "enabled" : null,
                CodeMode = options.CodeMode,
                FixMode = options.FixMode
            });

        return await _llmProvider.GenerateAsync(
            prompt,
            maxTokens: options.MaxOutputTokens,
            temperature: options.Temperature,
            cancellationToken: cancellationToken);
    }

    private ValidationPassResult ValidateCodeOutput(string message, string response, RequirementsExtraction requirements)
    {
        var validation = _validationPass.Validate(response, new ValidationContext
        {
            OriginalQuery = message,
            ExpectedType = OutputType.Code,
            ExpectsCode = true
        });

        foreach (var issue in ValidateAgainstRequirements(message, response, requirements))
        {
            validation.Issues.Add(issue);
            validation.IsValid = false;
        }

        return validation;
    }

    private static List<ValidationIssue> ValidateAgainstRequirements(string message, string response, RequirementsExtraction requirements)
    {
        var issues = new List<ValidationIssue>();
        var lowerMessage = message.ToLowerInvariant();
        var lowerResponse = response.ToLowerInvariant();

        if (lowerMessage.Contains("snake"))
        {
            var hasCanvas = lowerResponse.Contains("<canvas") || lowerResponse.Contains("canvas");
            var hasArrow = lowerResponse.Contains("arrow") || lowerResponse.Contains("keydown");
            if (!hasCanvas || !hasArrow)
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "SNAKE_MISMATCH",
                    Message = "Snake request must include a canvas and arrow-key input handling."
                });
            }
        }

        if (requirements.Runtime.Contains("canvas", StringComparison.OrdinalIgnoreCase))
        {
            if (!lowerResponse.Contains("<canvas") && !lowerResponse.Contains("canvas"))
            {
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Code = "MISSING_CANVAS",
                    Message = "Canvas runtime was requested but no canvas element or usage was found."
                });
            }
        }

        return issues;
    }

    private static RequirementsExtraction ParseRequirements(string message, string response)
    {
        var json = ExtractJsonObject(response);
        if (string.IsNullOrWhiteSpace(json))
        {
            return RequirementsExtraction.FromMessageFallback(message);
        }

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var requirements = new RequirementsExtraction
            {
                Task = root.GetPropertyOrDefault("task") ?? message,
                Language = root.GetPropertyOrDefault("language") ?? "",
                Platform = root.GetPropertyOrDefault("platform") ?? "unknown",
                Runtime = root.GetPropertyOrDefault("runtime") ?? "",
                Inputs = root.GetArrayOrEmpty("inputs"),
                Outputs = root.GetArrayOrEmpty("outputs"),
                Constraints = root.GetArrayOrEmpty("constraints"),
                Missing = root.GetArrayOrEmpty("missing")
            };

            if (string.Equals(requirements.Platform, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                requirements.Platform = RequirementsExtraction.DetectPlatformFromMessage(message);
            }

            return requirements;
        }
        catch
        {
            return RequirementsExtraction.FromMessageFallback(message);
        }
    }

    private static string ExtractJsonObject(string response)
    {
        var start = response.IndexOf('{');
        var end = response.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return response.Substring(start, end - start + 1);
    }

    private static bool IsCodeIntent(IntentType intent)
    {
        return intent is IntentType.CodeWrite or IntentType.CodeEdit or IntentType.CodeExplain;
    }

    private static bool IsToolIntent(IntentType intent)
    {
        return intent is IntentType.FileList or IntentType.FileCreate or IntentType.FileDelete or IntentType.ShellCommand or IntentType.Search;
    }

    private static bool HasToolSteps(PipelineDefinition pipeline, IReadOnlyCollection<PipelineStep> steps)
    {
        if (pipeline.Config.EnabledTools.Any())
            return true;

        return steps.Any(s =>
            s.Type == PipelineStepTypes.ToolLoop ||
            s.Type == PipelineStepTypes.ToolParse ||
            s.Type == PipelineStepTypes.ToolExecute);
    }

    private static string BuildTaskLock()
    {
        return "TASK LOCK: Follow the user's request exactly. Do not change the task.";
    }

    private static string BuildMissingInfoPrompt(RequirementsExtraction requirements)
    {
        var missingList = string.Join(", ", requirements.Missing);
        return $"Missing details: {missingList}. Please provide only those details so I can proceed.";
    }

    private static string BuildCodeDeveloperPrompt(RequirementsExtraction requirements)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Developer Prompt:");
        sb.AppendLine("Use the extracted requirements exactly. Do not add features.");
        sb.AppendLine($"Platform: {requirements.Platform}");
        if (!string.IsNullOrWhiteSpace(requirements.Language))
            sb.AppendLine($"Language: {requirements.Language}");
        if (!string.IsNullOrWhiteSpace(requirements.Runtime))
            sb.AppendLine($"Runtime: {requirements.Runtime}");
        if (requirements.Inputs.Count > 0)
            sb.AppendLine($"Inputs: {string.Join(", ", requirements.Inputs)}");
        if (requirements.Outputs.Count > 0)
            sb.AppendLine($"Outputs: {string.Join(", ", requirements.Outputs)}");
        if (requirements.Constraints.Count > 0)
            sb.AppendLine($"Constraints: {string.Join(", ", requirements.Constraints)}");
        return sb.ToString().Trim();
    }

    private static string BuildFixDeveloperPrompt(RequirementsExtraction requirements, ValidationPassResult validation, string response)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Developer Prompt:");
        sb.AppendLine("Fix the issues listed below while keeping the original task and requirements.");
        sb.AppendLine($"Platform: {requirements.Platform}");
        if (!string.IsNullOrWhiteSpace(requirements.Language))
            sb.AppendLine($"Language: {requirements.Language}");
        if (!string.IsNullOrWhiteSpace(requirements.Runtime))
            sb.AppendLine($"Runtime: {requirements.Runtime}");
        sb.AppendLine("Issues:");
        foreach (var issue in validation.Issues.Select(i => i.Message))
        {
            sb.AppendLine($"- {issue}");
        }
        sb.AppendLine("Previous Output:");
        sb.AppendLine(response);
        return sb.ToString().Trim();
    }

    private static bool ContainsToolCall(string text)
    {
        return text.Contains("```tool", StringComparison.OrdinalIgnoreCase);
    }

    private static string? AppendToolEnforcement(string? developerPrompt)
    {
        var enforcement = "TOOL CALL REQUIRED: Respond with only a tool block and execute the first required file operation. If using write_file, content must be complete and non-placeholder (min 200 chars).";
        if (string.IsNullOrWhiteSpace(developerPrompt))
            return enforcement;
        return developerPrompt.TrimEnd() + "\n" + enforcement;
    }

    private static bool IsPlaceholderContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return true;

        var lower = content.Trim().ToLowerInvariant();
        if (lower.Contains("code goes here") || lower.Contains("todo") || lower == "//")
            return true;

        return content.Trim().Length < 200;
    }

    private static bool ContainsBoltArtifact(string text)
    {
        return text.Contains("<boltArtifact", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<artifact_info", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("<boltAction", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsCodeBlocks(string text)
    {
        // Check for markdown code blocks with language hints that suggest file content
        return Regex.IsMatch(text, @"```(?:html|javascript|js|typescript|ts|css|json|python|py|java|csharp|cs|c\+\+|cpp|go|rust|ruby|php|sql|yaml|xml|sh|bash)\s*\n", RegexOptions.IgnoreCase);
    }

    private async IAsyncEnumerable<FactoryOutput> ExtractAndWriteCodeBlocksAsync(
        string response,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Extract code blocks with their language and try to determine filenames
        var codeBlockRegex = new Regex(
            @"(?:(?:create|file|named?|called)\s+[`""]?([^\s`""<>]+\.\w+)[`""]?.*?)?```(\w+)\s*\n(.*?)```",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var matches = codeBlockRegex.Matches(response);
        var fileIndex = 0;
        var writtenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            var explicitFilename = match.Groups[1].Success ? match.Groups[1].Value.Trim() : null;
            var language = match.Groups[2].Value.ToLowerInvariant();
            var content = match.Groups[3].Value;

            // Determine filename
            string filename;
            if (!string.IsNullOrEmpty(explicitFilename))
            {
                filename = explicitFilename;
            }
            else
            {
                // Generate filename based on language
                var extension = language switch
                {
                    "html" => ".html",
                    "javascript" or "js" => ".js",
                    "typescript" or "ts" => ".ts",
                    "css" => ".css",
                    "json" => ".json",
                    "python" or "py" => ".py",
                    "java" => ".java",
                    "csharp" or "cs" => ".cs",
                    "c++" or "cpp" => ".cpp",
                    "go" => ".go",
                    "rust" => ".rs",
                    "ruby" => ".rb",
                    "php" => ".php",
                    "sql" => ".sql",
                    "yaml" or "yml" => ".yaml",
                    "xml" => ".xml",
                    "sh" or "bash" => ".sh",
                    _ => ".txt"
                };

                // Try to find filename from surrounding text
                var surroundingMatch = Regex.Match(response.Substring(Math.Max(0, match.Index - 200), Math.Min(200, match.Index)),
                    @"[`""]([^\s`""<>]+\" + Regex.Escape(extension) + @")[`""]",
                    RegexOptions.IgnoreCase);

                if (surroundingMatch.Success)
                {
                    filename = surroundingMatch.Groups[1].Value;
                }
                else
                {
                    // Default filename
                    filename = language == "html" ? "index.html" :
                               language == "javascript" || language == "js" ? "index.js" :
                               language == "css" ? "styles.css" :
                               $"file{fileIndex}{extension}";
                }
            }

            // Avoid duplicate writes
            if (writtenFiles.Contains(filename))
            {
                var baseName = Path.GetFileNameWithoutExtension(filename);
                var ext = Path.GetExtension(filename);
                filename = $"{baseName}_{fileIndex}{ext}";
            }
            writtenFiles.Add(filename);

            // Normalize content
            content = content.Trim('\r', '\n').Trim();

            if (string.IsNullOrWhiteSpace(content) || content.Length < 10)
            {
                fileIndex++;
                continue;
            }

            // Write the file
            await foreach (var output in ExecuteWriteFileAsync(filename, content, cancellationToken))
            {
                yield return output;
            }

            fileIndex++;
        }

        // If we found code blocks but couldn't extract files, at least notify
        if (matches.Count > 0 && writtenFiles.Count == 0)
        {
            yield return new FactoryOutput
            {
                Type = FactoryOutputType.Progress,
                Content = "Code blocks detected but could not determine filenames. Please specify file names.",
                IsPartial = true
            };
        }
    }

    private async IAsyncEnumerable<FactoryOutput> ExecuteBoltArtifactsAsync(
        string response,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Regex captures: Group 1 = all attributes, Group 2 = body content
        var actionRegex = new Regex(
            "<boltAction\\s+([^>]*)>(.*?)</boltAction>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match match in actionRegex.Matches(response))
        {
            var attributes = match.Groups[1].Value;
            var actionBody = match.Groups[2].Value;

            // Extract type and filePath from attributes (not from body)
            var actionType = ExtractAttribute(attributes, "type")?.ToLowerInvariant();
            var filePath = ExtractAttribute(attributes, "filePath");

            if (actionType == "file" && !string.IsNullOrWhiteSpace(filePath))
            {
                // For file actions, the body IS the file content
                var fileContent = NormalizeBoltContent(actionBody);

                await foreach (var output in ExecuteWriteFileAsync(filePath.Trim(), fileContent, cancellationToken))
                {
                    yield return output;
                }
                continue;
            }

            if (actionType == "shell")
            {
                var command = NormalizeBoltContent(actionBody);
                if (string.IsNullOrWhiteSpace(command))
                {
                    continue;
                }

                await foreach (var output in ExecuteRunCommandAsync(command, cancellationToken))
                {
                    yield return output;
                }
            }
        }
    }

    private static string? ExtractAttribute(string attributes, string name)
    {
        var match = Regex.Match(attributes, $@"{name}=""([^""]*)""", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string NormalizeBoltContent(string content)
    {
        return content.Trim('\r', '\n').Trim();
    }

    private async IAsyncEnumerable<FactoryOutput> ExecuteWriteFileAsync(
        string path,
        string content,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolCall = new ToolCall
        {
            ToolName = "write_file",
            Arguments = new Dictionary<string, object>
            {
                ["path"] = path,
                ["content"] = content
            }
        };

        yield return new FactoryOutput
        {
            Type = FactoryOutputType.ToolCall,
            Content = $"Executing write_file for {path}...",
            IsPartial = true,
            Metadata = new Dictionary<string, object>
            {
                ["toolName"] = toolCall.ToolName,
                ["toolArguments"] = toolCall.Arguments
            }
        };

        var toolResult = await _toolRouter.ExecuteAsync(toolCall, cancellationToken);
        yield return new FactoryOutput
        {
            Type = FactoryOutputType.ToolResult,
            Content = toolResult.Success ? toolResult.Output : toolResult.Error ?? "Tool failed",
            IsPartial = true,
            Metadata = new Dictionary<string, object>
            {
                ["toolName"] = toolCall.ToolName,
                ["toolSuccess"] = toolResult.Success
            }
        };
    }

    private async IAsyncEnumerable<FactoryOutput> ExecuteRunCommandAsync(
        string command,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolCall = new ToolCall
        {
            ToolName = "run_command",
            Arguments = new Dictionary<string, object>
            {
                ["command"] = command
            }
        };

        yield return new FactoryOutput
        {
            Type = FactoryOutputType.ToolCall,
            Content = "Executing run_command...",
            IsPartial = true,
            Metadata = new Dictionary<string, object>
            {
                ["toolName"] = toolCall.ToolName,
                ["toolArguments"] = toolCall.Arguments
            }
        };

        var toolResult = await _toolRouter.ExecuteAsync(toolCall, cancellationToken);
        yield return new FactoryOutput
        {
            Type = FactoryOutputType.ToolResult,
            Content = toolResult.Success ? toolResult.Output : toolResult.Error ?? "Tool failed",
            IsPartial = true,
            Metadata = new Dictionary<string, object>
            {
                ["toolName"] = toolCall.ToolName,
                ["toolSuccess"] = toolResult.Success
            }
        };
    }

}

internal class RequirementsExtraction
{
    public string Task { get; set; } = "";
    public string Language { get; set; } = "";
    public string Platform { get; set; } = "unknown";
    public string Runtime { get; set; } = "";
    public List<string> Inputs { get; set; } = new();
    public List<string> Outputs { get; set; } = new();
    public List<string> Constraints { get; set; } = new();
    public List<string> Missing { get; set; } = new();

    public static RequirementsExtraction FromMessageFallback(string message)
    {
        return new RequirementsExtraction
        {
            Task = message,
            Platform = DetectPlatformFromMessage(message)
        };
    }

    public static string DetectPlatformFromMessage(string message)
    {
        var lower = message.ToLowerInvariant();
        if (lower.Contains("browser") || lower.Contains("html") || lower.Contains("canvas"))
            return "browser";
        if (lower.Contains("node") || lower.Contains("npm") || lower.Contains("express"))
            return "node";
        if (lower.Contains("ios") || lower.Contains("android") || lower.Contains("mobile"))
            return "mobile";
        if (lower.Contains("desktop") || lower.Contains("windows") || lower.Contains("mac"))
            return "desktop";
        if (lower.Contains("server") || lower.Contains("api"))
            return "server";
        return "unknown";
    }
}

internal static class JsonElementExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    public static List<string> GetArrayOrEmpty(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString() ?? "")
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
        }
        return new List<string>();
    }
}
