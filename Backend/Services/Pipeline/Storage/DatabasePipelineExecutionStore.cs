using LittleHelperAI.Data;
using LittleHelperAI.Data.Entities;
using LittleHelperAI.KingFactory.Pipeline.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

// Alias to avoid conflict with LittleHelperAI.Backend.Services.PipelineMetrics
using PipelineMetricsData = LittleHelperAI.KingFactory.Pipeline.Storage.PipelineMetrics;
using DailyMetricData = LittleHelperAI.KingFactory.Pipeline.Storage.DailyMetric;

namespace LittleHelperAI.Backend.Services.Pipeline.Storage;

/// <summary>
/// Database-backed implementation of execution logging.
/// </summary>
public sealed class DatabasePipelineExecutionStore : IPipelineExecutionStore
{
    private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
    private readonly ILogger<DatabasePipelineExecutionStore> _logger;

    public DatabasePipelineExecutionStore(
        IDbContextFactory<ApplicationDbContext> contextFactory,
        ILogger<DatabasePipelineExecutionStore> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<string> BeginExecutionAsync(
        string pipelineId,
        string? conversationId,
        int? userId,
        string? inputSummary,
        int stepCount,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var execution = new PipelineExecutionEntity
        {
            PipelineId = pipelineId,
            ConversationId = conversationId,
            UserId = userId,
            Status = "Running",
            StepCount = stepCount,
            InputSummary = inputSummary?.Length > 500 ? inputSummary.Substring(0, 500) : inputSummary
        };

        context.Set<PipelineExecutionEntity>().Add(execution);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Started execution {ExecutionId} for pipeline {PipelineId}", execution.Id, pipelineId);
        return execution.Id;
    }

    public async Task CompleteExecutionAsync(
        string executionId,
        bool success,
        string? errorMessage,
        long durationMs,
        int completedSteps,
        string? outputSummary,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Set<PipelineExecutionEntity>()
            .Where(e => e.Id == executionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Status, success ? "Completed" : "Failed")
                .SetProperty(e => e.CompletedAt, DateTime.UtcNow)
                .SetProperty(e => e.DurationMs, durationMs)
                .SetProperty(e => e.CompletedStepCount, completedSteps)
                .SetProperty(e => e.ErrorMessage, errorMessage)
                .SetProperty(e => e.OutputSummary, outputSummary != null && outputSummary.Length > 500
                    ? outputSummary.Substring(0, 500)
                    : outputSummary),
                cancellationToken);

        _logger.LogDebug("Completed execution {ExecutionId}: {Status}", executionId, success ? "Success" : "Failed");
    }

    public async Task RecordStepAsync(
        string executionId,
        string stepId,
        string stepType,
        int stepOrder,
        bool success,
        long durationMs,
        string? inputJson,
        string? outputJson,
        string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var stepLog = new PipelineStepLogEntity
        {
            ExecutionId = executionId,
            StepId = stepId,
            StepType = stepType,
            StepOrder = stepOrder,
            Status = success ? "Completed" : "Failed",
            CompletedAt = DateTime.UtcNow,
            DurationMs = durationMs,
            InputJson = inputJson,
            OutputJson = outputJson,
            ErrorMessage = errorMessage
        };

        context.Set<PipelineStepLogEntity>().Add(stepLog);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<ExecutionSummary>> GetExecutionsAsync(
        ExecutionFilter filter,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Set<PipelineExecutionEntity>().AsQueryable();

        if (!string.IsNullOrEmpty(filter.PipelineId))
        {
            query = query.Where(e => e.PipelineId == filter.PipelineId);
        }

        if (filter.UserId.HasValue)
        {
            query = query.Where(e => e.UserId == filter.UserId);
        }

        if (!string.IsNullOrEmpty(filter.Status))
        {
            query = query.Where(e => e.Status == filter.Status);
        }

        if (filter.StartDate.HasValue)
        {
            query = query.Where(e => e.StartedAt >= filter.StartDate);
        }

        if (filter.EndDate.HasValue)
        {
            query = query.Where(e => e.StartedAt <= filter.EndDate);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(e => e.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new ExecutionSummary
            {
                Id = e.Id,
                PipelineId = e.PipelineId,
                ConversationId = e.ConversationId,
                UserId = e.UserId,
                Status = e.Status,
                StartedAt = e.StartedAt,
                CompletedAt = e.CompletedAt,
                DurationMs = e.DurationMs,
                StepCount = e.StepCount,
                CompletedStepCount = e.CompletedStepCount,
                InputSummary = e.InputSummary
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<ExecutionSummary>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ExecutionDetails?> GetExecutionDetailsAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var execution = await context.Set<PipelineExecutionEntity>()
            .FirstOrDefaultAsync(e => e.Id == executionId, cancellationToken);

        if (execution == null)
        {
            return null;
        }

        var steps = await context.Set<PipelineStepLogEntity>()
            .Where(s => s.ExecutionId == executionId)
            .OrderBy(s => s.StepOrder)
            .Select(s => new StepLog
            {
                Id = s.Id,
                StepId = s.StepId,
                StepType = s.StepType,
                StepOrder = s.StepOrder,
                Status = s.Status,
                StartedAt = s.StartedAt,
                CompletedAt = s.CompletedAt,
                DurationMs = s.DurationMs,
                InputJson = s.InputJson,
                OutputJson = s.OutputJson,
                ErrorMessage = s.ErrorMessage
            })
            .ToListAsync(cancellationToken);

        return new ExecutionDetails
        {
            Id = execution.Id,
            PipelineId = execution.PipelineId,
            ConversationId = execution.ConversationId,
            UserId = execution.UserId,
            Status = execution.Status,
            StartedAt = execution.StartedAt,
            CompletedAt = execution.CompletedAt,
            DurationMs = execution.DurationMs,
            StepCount = execution.StepCount,
            CompletedStepCount = execution.CompletedStepCount,
            ErrorMessage = execution.ErrorMessage,
            InputSummary = execution.InputSummary,
            OutputSummary = execution.OutputSummary,
            Steps = steps
        };
    }

    public async Task<PipelineMetricsData> GetMetricsAsync(
        string? pipelineId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        startDate ??= DateTime.UtcNow.AddDays(-30);
        endDate ??= DateTime.UtcNow;

        var query = context.Set<PipelineMetricEntity>()
            .Where(m => m.Date >= startDate && m.Date <= endDate);

        if (!string.IsNullOrEmpty(pipelineId))
        {
            query = query.Where(m => m.PipelineId == pipelineId);
        }

        var metrics = await query.ToListAsync(cancellationToken);

        var dailyMetrics = metrics
            .GroupBy(m => m.Date.Date)
            .Select(g => new DailyMetricData
            {
                Date = g.Key,
                TotalExecutions = g.Sum(m => m.TotalExecutions),
                SuccessCount = g.Sum(m => m.SuccessCount),
                FailureCount = g.Sum(m => m.FailureCount),
                AvgDurationMs = (long?)g.Where(m => m.AvgDurationMs.HasValue).Average(m => m.AvgDurationMs)
            })
            .OrderBy(d => d.Date)
            .ToList();

        return new PipelineMetricsData
        {
            TotalExecutions = metrics.Sum(m => m.TotalExecutions),
            SuccessCount = metrics.Sum(m => m.SuccessCount),
            FailureCount = metrics.Sum(m => m.FailureCount),
            AvgDurationMs = metrics.Where(m => m.AvgDurationMs.HasValue).Any()
                ? (long?)metrics.Where(m => m.AvgDurationMs.HasValue).Average(m => m.AvgDurationMs)
                : null,
            MinDurationMs = metrics.Where(m => m.MinDurationMs.HasValue).Any()
                ? metrics.Min(m => m.MinDurationMs)
                : null,
            MaxDurationMs = metrics.Where(m => m.MaxDurationMs.HasValue).Any()
                ? metrics.Max(m => m.MaxDurationMs)
                : null,
            TotalStepsExecuted = metrics.Sum(m => m.TotalStepsExecuted),
            TotalToolCalls = metrics.Sum(m => m.TotalToolCalls),
            DailyMetrics = dailyMetrics
        };
    }

    public async Task UpdateMetricsAsync(
        string pipelineId,
        DateTime date,
        bool success,
        long durationMs,
        int stepsExecuted,
        int toolCalls,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var metricDate = date.Date;

        var metric = await context.Set<PipelineMetricEntity>()
            .FirstOrDefaultAsync(m => m.PipelineId == pipelineId && m.Date == metricDate, cancellationToken);

        if (metric == null)
        {
            metric = new PipelineMetricEntity
            {
                PipelineId = pipelineId,
                Date = metricDate
            };
            context.Set<PipelineMetricEntity>().Add(metric);
        }

        metric.TotalExecutions++;
        if (success)
        {
            metric.SuccessCount++;
        }
        else
        {
            metric.FailureCount++;
        }

        // Update average duration
        if (metric.AvgDurationMs.HasValue)
        {
            // Running average
            metric.AvgDurationMs = (metric.AvgDurationMs * (metric.TotalExecutions - 1) + durationMs) / metric.TotalExecutions;
        }
        else
        {
            metric.AvgDurationMs = durationMs;
        }

        // Update min/max
        if (!metric.MinDurationMs.HasValue || durationMs < metric.MinDurationMs)
        {
            metric.MinDurationMs = durationMs;
        }
        if (!metric.MaxDurationMs.HasValue || durationMs > metric.MaxDurationMs)
        {
            metric.MaxDurationMs = durationMs;
        }

        metric.TotalStepsExecuted += stepsExecuted;
        metric.TotalToolCalls += toolCalls;

        await context.SaveChangesAsync(cancellationToken);
    }
}
