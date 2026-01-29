using LittleHelperAI.KingFactory.Prompts;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LittleHelperAI.KingFactory.Pipeline;

/// <summary>
/// Registry for managing pipeline definitions.
/// </summary>
public interface IPipelineRegistry
{
    /// <summary>
    /// Get all pipelines.
    /// </summary>
    IReadOnlyList<PipelineDefinition> GetAll();

    /// <summary>
    /// Get a pipeline by ID.
    /// </summary>
    PipelineDefinition? GetById(string id);

    /// <summary>
    /// Get a pipeline by name.
    /// </summary>
    PipelineDefinition? GetByName(string name);

    /// <summary>
    /// Get the active/default pipeline.
    /// </summary>
    PipelineDefinition GetActivePipeline();

    /// <summary>
    /// Set the active pipeline by ID.
    /// </summary>
    void SetActivePipeline(string id);

    /// <summary>
    /// Add or update a pipeline.
    /// </summary>
    void Save(PipelineDefinition pipeline);

    /// <summary>
    /// Delete a pipeline.
    /// </summary>
    bool Delete(string id);

    /// <summary>
    /// Find a pipeline that matches keywords in the message.
    /// Returns the primary pipeline if no keywords match.
    /// </summary>
    PipelineDefinition GetPipelineForMessage(string message);

    /// <summary>
    /// Get the primary (default) pipeline ID.
    /// </summary>
    string PrimaryPipelineId { get; }
}

/// <summary>
/// In-memory pipeline registry with the King Pipeline as default.
/// </summary>
public class PipelineRegistry : IPipelineRegistry
{
    private readonly ILogger<PipelineRegistry> _logger;
    private readonly Dictionary<string, PipelineDefinition> _pipelines = new();
    private string _activePipelineId;
    private string _primaryPipelineId;
    private readonly string _pipelinesFilePath;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string PrimaryPipelineId => _primaryPipelineId;

    public PipelineRegistry(ILogger<PipelineRegistry> logger)
    {
        _logger = logger;

        // Set up file path for persistence
        var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittleHelperAI");
        Directory.CreateDirectory(appDataPath);
        _pipelinesFilePath = Path.Combine(appDataPath, "pipelines.json");

        // Create the King Pipeline as the default
        var kingPipeline = CreateKingPipeline();
        _pipelines[kingPipeline.Id] = kingPipeline;
        _activePipelineId = kingPipeline.Id;
        _primaryPipelineId = kingPipeline.Id; // King Pipeline is the primary by default

        // Add some additional pipelines for reference
        AddDefaultPipelines();

        // Load saved pipelines from file
        LoadPipelinesFromFile();

        // Ensure built-in pipelines use the latest code defaults
        _pipelines["bolt-parody"] = CreateBoltParodyPipeline();

        // Force bolt-parody as primary/active by default for reliability
        _primaryPipelineId = "bolt-parody";
        _activePipelineId = "bolt-parody";
        SavePipelinesToFile();

        _logger.LogInformation("Pipeline registry initialized with {Count} pipelines. Primary: {PrimaryPipeline}",
            _pipelines.Count, kingPipeline.Name);
    }

    /// <summary>
    /// Set the primary pipeline by ID.
    /// </summary>
    public void SetPrimaryPipeline(string id)
    {
        if (!_pipelines.ContainsKey(id))
        {
            throw new ArgumentException($"Pipeline '{id}' not found");
        }

        _primaryPipelineId = id;
        _logger.LogInformation("Primary pipeline changed to: {PipelineId}", id);

        // Persist to file
        SavePipelinesToFile();
    }

    /// <summary>
    /// Find a pipeline that matches keywords in the message.
    /// </summary>
    public PipelineDefinition GetPipelineForMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return GetActivePipeline();
        }

        var lowerMessage = message.ToLowerInvariant();

        // Check all pipelines except primary for keyword matches
        foreach (var pipeline in _pipelines.Values.Where(p => p.Id != _primaryPipelineId && p.Status == PipelineStatus.Active))
        {
            var keywords = pipeline.Config.TriggerKeywords;
            if (keywords != null && keywords.Any())
            {
                foreach (var keyword in keywords)
                {
                    if (!string.IsNullOrWhiteSpace(keyword) && lowerMessage.Contains(keyword.ToLowerInvariant()))
                    {
                        _logger.LogDebug("Message matched keyword '{Keyword}' for pipeline '{Pipeline}'", keyword, pipeline.Name);
                        return pipeline;
                    }
                }
            }
        }

        // No keyword match, return primary pipeline
        return _pipelines.TryGetValue(_primaryPipelineId, out var primary) ? primary : GetActivePipeline();
    }

    /// <summary>
    /// Creates the King Pipeline - the main AI assistant pipeline.
    /// </summary>
    private PipelineDefinition CreateKingPipeline()
    {
        return new PipelineDefinition
        {
            Id = "king-pipeline",
            Name = "King Pipeline",
            Description = "Main AI assistant pipeline with full tool support, conversation injection, and all coding prompts.",
            Version = "1.0.0",
            Status = PipelineStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Steps = new List<PipelineStep>
            {
                // Step 1: Load project memory/context
                new()
                {
                    Id = "load-memory",
                    Type = PipelineStepTypes.MemoryLoad,
                    Description = "Load project memory and context",
                    Order = 1
                },

                // Step 2: Inject system/core prompt
                new()
                {
                    Id = "inject-core",
                    Type = PipelineStepTypes.InjectCorePrompt,
                    Description = "Inject core system prompt with AI identity and capabilities",
                    DependsOn = new List<string> { "load-memory" },
                    Order = 2
                },

                // Step 3: Inject tools prompt
                new()
                {
                    Id = "inject-tools",
                    Type = PipelineStepTypes.InjectToolsPrompt,
                    Description = "Inject tools prompt with available tools and usage instructions",
                    DependsOn = new List<string> { "inject-core" },
                    Order = 3
                },

                // Step 4: Inject planning prompt
                new()
                {
                    Id = "inject-planning",
                    Type = PipelineStepTypes.InjectPlanningPrompt,
                    Description = "Inject planning prompt for complex tasks",
                    DependsOn = new List<string> { "inject-tools" },
                    Order = 4
                },

                // Step 5: Inject project context
                new()
                {
                    Id = "inject-project",
                    Type = PipelineStepTypes.InjectProjectContext,
                    Description = "Inject current project context and file structure",
                    DependsOn = new List<string> { "inject-planning" },
                    Order = 5
                },

                // Step 6: Inject conversation history
                new()
                {
                    Id = "inject-conversation",
                    Type = PipelineStepTypes.InjectConversation,
                    Description = "Inject windowed conversation history",
                    DependsOn = new List<string> { "inject-project" },
                    Order = 6
                },

                // Step 7: Analyze intent
                new()
                {
                    Id = "analyze-intent",
                    Type = PipelineStepTypes.AnalyzeIntent,
                    Description = "Classify user intent to determine processing mode",
                    DependsOn = new List<string> { "inject-conversation" },
                    Order = 7
                },

                // Step 8: LLM streaming generation
                new()
                {
                    Id = "llm-stream",
                    Type = PipelineStepTypes.LlmStream,
                    Description = "Stream response from LLM",
                    DependsOn = new List<string> { "analyze-intent" },
                    Order = 8
                },

                // Step 9: Tool loop (parse and execute tools iteratively)
                new()
                {
                    Id = "tool-loop",
                    Type = PipelineStepTypes.ToolLoop,
                    Description = "Parse tool calls from response and execute them in a loop",
                    DependsOn = new List<string> { "llm-stream" },
                    Order = 9,
                    Config = new Dictionary<string, object>
                    {
                        ["maxIterations"] = 10
                    }
                },

                // Step 10: Validate output
                new()
                {
                    Id = "validate-output",
                    Type = PipelineStepTypes.ValidateOutput,
                    Description = "Validate the final response",
                    DependsOn = new List<string> { "tool-loop" },
                    Order = 10
                },

                // Step 11: Save memory
                new()
                {
                    Id = "save-memory",
                    Type = PipelineStepTypes.MemorySave,
                    Description = "Save updated project memory",
                    DependsOn = new List<string> { "validate-output" },
                    Order = 11
                }
            },
            Config = new PipelineConfig
            {
                IncludedPrompts = new List<string>
                {
                    "Core",
                    "Tools",
                    "Planning",
                    "Validation",
                    "Streaming"
                },
                EnabledTools = new List<string>
                {
                    "write_file",
                    "read_file",
                    "list_files",
                    "run_command",
                    "fetch"
                },
                InjectConversation = true,
                MaxContextTokens = 4096,
                MaxToolIterations = 10,
                Temperature = 0.15f,
                MaxOutputTokens = 2048,
                EnablePlanning = true,
                EnableValidation = true
            }
        };
    }

    /// <summary>
    /// Adds some additional default pipelines for different use cases.
    /// </summary>
    private void AddDefaultPipelines()
    {
        // Simple chat pipeline (no tools)
        _pipelines["simple-chat"] = new PipelineDefinition
        {
            Id = "simple-chat",
            Name = "Simple Chat",
            Description = "Basic conversational pipeline without tool execution.",
            Version = "1.0.0",
            Status = PipelineStatus.Active,
            Steps = new List<PipelineStep>
            {
                new() { Id = "inject-core", Type = PipelineStepTypes.InjectCorePrompt, Order = 1 },
                new() { Id = "inject-conversation", Type = PipelineStepTypes.InjectConversation, DependsOn = new() { "inject-core" }, Order = 2 },
                new() { Id = "llm-stream", Type = PipelineStepTypes.LlmStream, DependsOn = new() { "inject-conversation" }, Order = 3 }
            },
            Config = new PipelineConfig
            {
                TriggerKeywords = new() { "just chat", "no code", "talk to me", "conversation only" },
                IncludedPrompts = new() { "Core", "Streaming" },
                EnabledTools = new(),
                InjectConversation = true,
                MaxContextTokens = 4096,
                MaxToolIterations = 0,
                EnablePlanning = false,
                EnableValidation = false
            }
        };

        // Bolt-style parody pipeline
        _pipelines["bolt-parody"] = CreateBoltParodyPipeline();

        // Code analysis pipeline
        _pipelines["code-analysis"] = new PipelineDefinition
        {
            Id = "code-analysis",
            Name = "Code Analysis",
            Description = "Pipeline for analyzing code structure and dependencies.",
            Version = "1.0.0",
            Status = PipelineStatus.Active,
            Steps = new List<PipelineStep>
            {
                new() { Id = "inject-core", Type = PipelineStepTypes.InjectCorePrompt, Order = 1 },
                new() { Id = "inject-tools", Type = PipelineStepTypes.InjectToolsPrompt, DependsOn = new() { "inject-core" }, Order = 2 },
                new() { Id = "scan-project", Type = "analysis.scan-project-structure", DependsOn = new() { "inject-tools" }, Order = 3 },
                new() { Id = "analyze-deps", Type = "analysis.analyze-dependencies", DependsOn = new() { "scan-project" }, Order = 4 },
                new() { Id = "llm-stream", Type = PipelineStepTypes.LlmStream, DependsOn = new() { "analyze-deps" }, Order = 5 }
            },
            Config = new PipelineConfig
            {
                TriggerKeywords = new() { "analyze code", "code review", "scan project", "check dependencies", "review my code" },
                IncludedPrompts = new() { "Core", "Tools" },
                EnabledTools = new() { "read_file", "list_files" },
                InjectConversation = true,
                MaxContextTokens = 8192,
                EnablePlanning = true,
                EnableValidation = false
            }
        };

        // Build pipeline
        _pipelines["build-test"] = new PipelineDefinition
        {
            Id = "build-test",
            Name = "Build & Test",
            Description = "Pipeline for building and testing projects.",
            Version = "1.0.0",
            Status = PipelineStatus.Active,
            Steps = new List<PipelineStep>
            {
                new() { Id = "detect-build", Type = PipelineStepTypes.BuildDetect, Order = 1 },
                new() { Id = "restore", Type = PipelineStepTypes.BuildRestore, DependsOn = new() { "detect-build" }, Order = 2 },
                new() { Id = "build", Type = PipelineStepTypes.BuildExecute, DependsOn = new() { "restore" }, Order = 3 },
                new() { Id = "test", Type = "verify.test", DependsOn = new() { "build" }, Order = 4 }
            },
            Config = new PipelineConfig
            {
                TriggerKeywords = new() { "build project", "run tests", "compile", "dotnet build", "npm build", "run build" },
                IncludedPrompts = new(),
                EnabledTools = new() { "run_command" },
                InjectConversation = false,
                EnablePlanning = false,
                EnableValidation = true
            }
        };
    }

    private static PipelineDefinition CreateBoltParodyPipeline()
    {
        return new PipelineDefinition
        {
            Id = "bolt-parody",
            Name = "Bolt Parody",
            Description = "Bolt-style system constraints and artifact instructions (parody).",
            Version = "1.0.0",
            Status = PipelineStatus.Active,
            Steps = new List<PipelineStep>
            {
                new() { Id = "inject-core", Type = PipelineStepTypes.InjectCorePrompt, Order = 1 },
                new() { Id = "inject-conversation", Type = PipelineStepTypes.InjectConversation, DependsOn = new() { "inject-core" }, Order = 2 },
                new() { Id = "llm-stream", Type = PipelineStepTypes.LlmStream, DependsOn = new() { "inject-conversation" }, Order = 3 }
            },
            Config = new PipelineConfig
            {
                TriggerKeywords = new() { "bolt", "parody", "bolt-style", "bolt prompt" },
                IncludedPrompts = new() { "Core" },
                EnabledTools = new() { "write_file", "read_file", "list_files", "run_command", "fetch" },
                InjectConversation = false,
                MaxContextTokens = 4096,
                MaxToolIterations = 10,
                Temperature = 0.2f,
                MaxOutputTokens = 2048,
                EnablePlanning = false,
                EnableValidation = false,
                PromptOverrides = new PromptOverrides
                {
                    SystemPrompt = BoltParodyPrompt.DeveloperPrompt
                }
            }
        };
    }

    public IReadOnlyList<PipelineDefinition> GetAll()
    {
        return _pipelines.Values.OrderBy(p => p.Name).ToList();
    }

    public PipelineDefinition? GetById(string id)
    {
        return _pipelines.TryGetValue(id, out var pipeline) ? pipeline : null;
    }

    public PipelineDefinition? GetByName(string name)
    {
        return _pipelines.Values.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public PipelineDefinition GetActivePipeline()
    {
        if (_pipelines.TryGetValue(_activePipelineId, out var pipeline))
        {
            return pipeline;
        }

        // Fallback to first active pipeline
        var firstActive = _pipelines.Values.FirstOrDefault(p => p.Status == PipelineStatus.Active);
        if (firstActive != null)
        {
            _activePipelineId = firstActive.Id;
            return firstActive;
        }

        // If no active pipelines, return the first one
        var first = _pipelines.Values.First();
        _activePipelineId = first.Id;
        return first;
    }

    public void SetActivePipeline(string id)
    {
        if (!_pipelines.ContainsKey(id))
        {
            throw new ArgumentException($"Pipeline '{id}' not found");
        }

        _activePipelineId = id;
        _logger.LogInformation("Active pipeline changed to: {PipelineId}", id);
    }

    public void Save(PipelineDefinition pipeline)
    {
        pipeline.UpdatedAt = DateTime.UtcNow;

        if (!_pipelines.ContainsKey(pipeline.Id))
        {
            pipeline.CreatedAt = DateTime.UtcNow;
            _logger.LogInformation("Created new pipeline: {PipelineName} ({PipelineId})", pipeline.Name, pipeline.Id);
        }
        else
        {
            _logger.LogInformation("Updated pipeline: {PipelineName} ({PipelineId})", pipeline.Name, pipeline.Id);
        }

        _pipelines[pipeline.Id] = pipeline;

        // Persist to file
        SavePipelinesToFile();
    }

    public bool Delete(string id)
    {
        if (id == "king-pipeline")
        {
            _logger.LogWarning("Cannot delete the King Pipeline");
            return false;
        }

        if (_pipelines.Remove(id))
        {
            _logger.LogInformation("Deleted pipeline: {PipelineId}", id);

            // If deleted the active pipeline, switch to King Pipeline
            if (_activePipelineId == id)
            {
                _activePipelineId = "king-pipeline";
            }

            // Persist to file
            SavePipelinesToFile();

            return true;
        }

        return false;
    }

    /// <summary>
    /// Save all user-created pipelines to a JSON file.
    /// Built-in pipelines (king-pipeline, simple-chat, code-analysis, build-test) are not saved.
    /// </summary>
    private void SavePipelinesToFile()
    {
        try
        {
            var builtInIds = new HashSet<string> { "king-pipeline", "simple-chat", "code-analysis", "build-test" };
            var userPipelines = _pipelines.Values
                .Where(p => !builtInIds.Contains(p.Id))
                .ToList();

            var data = new PipelinePersistenceData
            {
                Pipelines = userPipelines,
                PrimaryPipelineId = _primaryPipelineId,
                ActivePipelineId = _activePipelineId
            };

            var json = JsonSerializer.Serialize(data, _jsonOptions);
            File.WriteAllText(_pipelinesFilePath, json);

            _logger.LogDebug("Saved {Count} user pipelines to file", userPipelines.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save pipelines to file");
        }
    }

    /// <summary>
    /// Load user-created pipelines from a JSON file.
    /// </summary>
    private void LoadPipelinesFromFile()
    {
        try
        {
            if (!File.Exists(_pipelinesFilePath))
            {
                _logger.LogDebug("No pipelines file found at {Path}", _pipelinesFilePath);
                return;
            }

            var json = File.ReadAllText(_pipelinesFilePath);
            var data = JsonSerializer.Deserialize<PipelinePersistenceData>(json, _jsonOptions);

            if (data?.Pipelines != null)
            {
                var builtInIds = new HashSet<string> { "king-pipeline", "simple-chat", "code-analysis", "build-test", "bolt-parody" };
                foreach (var pipeline in data.Pipelines)
                {
                    if (builtInIds.Contains(pipeline.Id))
                    {
                        continue;
                    }
                    _pipelines[pipeline.Id] = pipeline;
                }

                _logger.LogInformation("Loaded {Count} user pipelines from file", data.Pipelines.Count);
            }

            // Restore primary pipeline if it exists
            if (!string.IsNullOrEmpty(data?.PrimaryPipelineId) && _pipelines.ContainsKey(data.PrimaryPipelineId))
            {
                _primaryPipelineId = data.PrimaryPipelineId;
            }

            // Restore active pipeline if it exists
            if (!string.IsNullOrEmpty(data?.ActivePipelineId) && _pipelines.ContainsKey(data.ActivePipelineId))
            {
                _activePipelineId = data.ActivePipelineId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load pipelines from file");
        }
    }
}

/// <summary>
/// Data structure for pipeline persistence.
/// </summary>
public class PipelinePersistenceData
{
    public List<PipelineDefinition> Pipelines { get; set; } = new();
    public string? PrimaryPipelineId { get; set; }
    public string? ActivePipelineId { get; set; }
}
