using LittleHelperAI.KingFactory.Models;
using System.Diagnostics;

namespace LittleHelperAI.KingFactory.Pipeline;

/// <summary>
/// Helper for tracking pipeline stage timing.
/// </summary>
public class PipelineStopwatch
{
    private readonly Stopwatch _totalStopwatch;
    private readonly PipelineTimingResult _result;
    private StageTimingData? _currentStage;
    private Stopwatch? _stageStopwatch;
    private Stopwatch? _ttftStopwatch;
    private bool _ttftRecorded;
    private int _tokenCount;
    private long _toolExecutionMs;

    public PipelineStopwatch(string pipelineId, string pipelineName, string? conversationId = null)
    {
        _totalStopwatch = Stopwatch.StartNew();
        _result = new PipelineTimingResult
        {
            PipelineId = pipelineId,
            PipelineName = pipelineName,
            ConversationId = conversationId,
            StartedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets the current timing result.
    /// </summary>
    public PipelineTimingResult Result => _result;

    /// <summary>
    /// Total elapsed time.
    /// </summary>
    public TimeSpan Elapsed => _totalStopwatch.Elapsed;

    /// <summary>
    /// Start timing a new stage.
    /// </summary>
    public void StartStage(string stageName)
    {
        // Complete any existing stage
        CompleteCurrentStage();

        _currentStage = new StageTimingData
        {
            StageName = stageName,
            StartedAt = DateTime.UtcNow
        };
        _stageStopwatch = Stopwatch.StartNew();
    }

    /// <summary>
    /// Complete the current stage.
    /// </summary>
    public void CompleteCurrentStage(bool success = true, string? error = null)
    {
        if (_currentStage == null || _stageStopwatch == null)
            return;

        _stageStopwatch.Stop();
        _currentStage.CompletedAt = DateTime.UtcNow;
        _currentStage.Success = success;
        _currentStage.Error = error;

        _result.Stages.Add(_currentStage);
        _currentStage = null;
        _stageStopwatch = null;
    }

    /// <summary>
    /// Add metadata to the current stage.
    /// </summary>
    public void AddStageMetadata(string key, object value)
    {
        if (_currentStage == null)
            return;

        _currentStage.Metadata ??= new Dictionary<string, object>();
        _currentStage.Metadata[key] = value;
    }

    /// <summary>
    /// Start tracking time to first token.
    /// </summary>
    public void StartTtftTracking()
    {
        if (!_ttftRecorded)
        {
            _ttftStopwatch = Stopwatch.StartNew();
        }
    }

    /// <summary>
    /// Record the first token time.
    /// </summary>
    public void RecordFirstToken()
    {
        if (!_ttftRecorded && _ttftStopwatch != null)
        {
            _ttftStopwatch.Stop();
            _result.TtftMs = _ttftStopwatch.ElapsedMilliseconds;
            _ttftRecorded = true;
        }
    }

    /// <summary>
    /// Record a generated token.
    /// </summary>
    public void RecordToken()
    {
        if (!_ttftRecorded)
        {
            RecordFirstToken();
        }
        _tokenCount++;
    }

    /// <summary>
    /// Record tool execution time.
    /// </summary>
    public void RecordToolExecution(TimeSpan duration)
    {
        _result.ToolCallCount++;
        _toolExecutionMs += (long)duration.TotalMilliseconds;
    }

    /// <summary>
    /// Create a scoped timer for a stage.
    /// </summary>
    public IDisposable TimeStage(string stageName)
    {
        StartStage(stageName);
        return new StageScope(this);
    }

    /// <summary>
    /// Create a scoped timer for tool execution.
    /// </summary>
    public IDisposable TimeToolExecution()
    {
        return new ToolExecutionScope(this);
    }

    /// <summary>
    /// Complete the pipeline timing.
    /// </summary>
    public PipelineTimingResult Complete(bool success = true)
    {
        CompleteCurrentStage();

        _totalStopwatch.Stop();
        _result.CompletedAt = DateTime.UtcNow;
        _result.Success = success;
        _result.TotalTokens = _tokenCount > 0 ? _tokenCount : null;
        _result.ToolExecutionMs = _toolExecutionMs;

        if (_tokenCount > 0 && _totalStopwatch.Elapsed.TotalSeconds > 0)
        {
            // Calculate TPS excluding tool execution time
            var generationTimeMs = _totalStopwatch.ElapsedMilliseconds - _toolExecutionMs;
            if (generationTimeMs > 0)
            {
                _result.TokensPerSecond = _tokenCount / (generationTimeMs / 1000.0);
            }
        }

        return _result;
    }

    private sealed class StageScope : IDisposable
    {
        private readonly PipelineStopwatch _stopwatch;
        private bool _disposed;

        public StageScope(PipelineStopwatch stopwatch)
        {
            _stopwatch = stopwatch;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _stopwatch.CompleteCurrentStage();
            }
        }
    }

    private sealed class ToolExecutionScope : IDisposable
    {
        private readonly PipelineStopwatch _stopwatch;
        private readonly Stopwatch _timer;
        private bool _disposed;

        public ToolExecutionScope(PipelineStopwatch stopwatch)
        {
            _stopwatch = stopwatch;
            _timer = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _timer.Stop();
                _stopwatch.RecordToolExecution(_timer.Elapsed);
            }
        }
    }
}
