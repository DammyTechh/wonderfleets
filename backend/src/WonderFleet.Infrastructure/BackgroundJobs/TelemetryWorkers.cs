using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Application.Features.Telemetry;
using WonderFleet.Domain.Enums;
using WonderFleet.Infrastructure.Integrations.Firebase;

namespace WonderFleet.Infrastructure.BackgroundJobs;

/// Polls the Realtime Database and ingests every device document.
internal sealed class TelemetryPollingWorker(
    IServiceScopeFactory scopeFactory,
    TelemetrySyncState syncState,
    IClock clock,
    IOptions<TelemetryOptions> telemetryOptions,
    IOptions<FirebaseOptions> firebaseOptions,
    ILogger<TelemetryPollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = telemetryOptions.Value;
        if (!options.PollingEnabled || !firebaseOptions.Value.IsConfigured)
        {
            logger.LogInformation("Telemetry polling is disabled (PollingEnabled={Enabled}, Firebase configured={Configured})",
                options.PollingEnabled, firebaseOptions.Value.IsConfigured);
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(options.PollIntervalSeconds, 5, 600));
        using var timer = new PeriodicTimer(interval);
        logger.LogInformation("Telemetry polling every {Seconds}s", interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var leader = scope.ServiceProvider.GetRequiredService<LeaderLock>();
                await leader.RunExclusiveAsync(LeaderLock.TelemetryPolling, PollAsync, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                syncState.PollFailed(clock.UtcNow, ex.Message);
                logger.LogError(ex, "Telemetry poll failed");
            }
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        IReadOnlyList<DeviceSnapshot> snapshots;
        using (var readScope = scopeFactory.CreateScope())
        {
            var gateway = readScope.ServiceProvider.GetRequiredService<IDeviceCloudGateway>();
            snapshots = await gateway.ReadAllSnapshotsAsync(ct);
        }
        syncState.PollSucceeded(clock.UtcNow, snapshots.Count);

        foreach (var snapshot in snapshots)
        {
            try
            {
                // A fresh scope per device keeps one bad payload from poisoning the whole batch.
                using var scope = scopeFactory.CreateScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<ITelemetryIngestionService>();
                await ingestion.IngestAsync(snapshot, TelemetrySource.Firebase, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Ingesting telemetry for {Key} failed", snapshot.FirebaseKey);
            }
        }
    }
}

/// Runs the offline/stoppage/delay checks that telemetry alone cannot detect.
internal sealed class TelemetryMonitorWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<AlertingOptions> options,
    ILogger<TelemetryMonitorWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.MonitorIntervalSeconds, 15, 900));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var leader = scope.ServiceProvider.GetRequiredService<LeaderLock>();
                await leader.RunExclusiveAsync(LeaderLock.Monitoring, async ct =>
                {
                    using var inner = scopeFactory.CreateScope();
                    await inner.ServiceProvider.GetRequiredService<ITelemetryMonitor>().RunOnceAsync(ct);
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telemetry monitor run failed");
            }
        }
    }
}
