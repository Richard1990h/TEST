using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LittleHelperAI.Backend.Services;

/// <summary>
/// ADD-ONLY: creates daily snapshots on a timer and prunes old ones.
/// Controlled by config:
///   Snapshots:EnableDaily (bool, default true)
///   Snapshots:HourUtc (int, default 0)
///   Snapshots:KeepDays (int, default 30)
/// </summary>
public sealed class SnapshotHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SnapshotHostedService> _logger;

    public SnapshotHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<SnapshotHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _config.GetValue<bool?>("Snapshots:EnableDaily") ?? true;
        if (!enabled)
        {
            _logger.LogInformation("[SNAPSHOT] Daily snapshots disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hourUtc = Math.Clamp(
                    _config.GetValue<int?>("Snapshots:HourUtc") ?? 0,
                    0,
                    23);

                var now = DateTime.UtcNow;
                var next = new DateTime(
                    now.Year,
                    now.Month,
                    now.Day,
                    hourUtc,
                    0,
                    0,
                    DateTimeKind.Utc);

                if (now >= next)
                    next = next.AddDays(1);

                var delay = next - now;

                _logger.LogInformation(
                    "[SNAPSHOT] Next daily snapshot at {Next} (in {Delay})",
                    next,
                    delay);

                await Task.Delay(delay, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                // 🔑 CREATE SCOPE PER RUN (FIX)
                using var scope = _scopeFactory.CreateScope();
                var snapshots = scope.ServiceProvider
                    .GetRequiredService<SnapshotService>();

                await snapshots.CreateSnapshotAsync(stoppingToken);

                var keepDays = Math.Max(
                    1,
                    _config.GetValue<int?>("Snapshots:KeepDays") ?? 30);

                snapshots.PruneOlderThan(TimeSpan.FromDays(keepDays));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SNAPSHOT] Daily snapshot loop error");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}
