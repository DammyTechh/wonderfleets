using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using WonderFleet.Application.Common.Interfaces;
using WonderFleet.Application.Common.Options;
using WonderFleet.Domain.Enums;
using WonderFleet.Infrastructure.Persistence;

namespace WonderFleet.Infrastructure.BackgroundJobs;

/// Transactional outbox dispatcher. Rows are claimed with FOR UPDATE SKIP LOCKED so several
/// instances can dispatch concurrently without sending an alert twice.
internal sealed class NotificationDispatchWorker(
    IServiceScopeFactory scopeFactory,
    NpgsqlDataSource dataSource,
    IOptions<AlertingOptions> options,
    ILogger<NotificationDispatchWorker> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private const int StuckMinutes = 10;

    private const string ClaimSql = """
        WITH stuck AS (
            UPDATE notification_deliveries
            SET status = 'Pending'
            WHERE status = 'Sending' AND next_attempt_at < now() - make_interval(mins => @stuck)
            RETURNING id
        ), claimed AS (
            SELECT id FROM notification_deliveries
            WHERE status = 'Pending' AND next_attempt_at <= now()
            ORDER BY next_attempt_at
            LIMIT @take
            FOR UPDATE SKIP LOCKED
        )
        UPDATE notification_deliveries d
        SET status = 'Sending', next_attempt_at = now()
        FROM claimed
        WHERE d.id = claimed.id
        RETURNING d.id
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.DispatchIntervalSeconds, 2, 120));
        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) == BatchSize && !stoppingToken.IsCancellationRequested)
                {
                    // Keep draining while the backlog is full-sized.
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Notification dispatch failed");
            }
        }
    }

    private async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        var ids = await ClaimAsync(ct);
        if (ids.Count == 0) return 0;

        using var scope = scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var clock = provider.GetRequiredService<IClock>();
        var email = provider.GetRequiredService<IEmailSender>();
        var sms = provider.GetRequiredService<ISmsSender>();

        var deliveries = await db.NotificationDeliveries.Where(d => ids.Contains(d.Id)).ToListAsync(ct);
        foreach (var delivery in deliveries)
        {
            SendResult result;
            try
            {
                result = delivery.Channel switch
                {
                    NotificationChannel.Email => await email.SendAsync(
                        new EmailMessage(delivery.Recipient, delivery.Subject ?? "WonderFleet", delivery.Body, delivery.TextBody), ct),
                    NotificationChannel.Sms => await sms.SendAsync(delivery.Recipient, delivery.TextBody ?? delivery.Body, ct),
                    _ => new SendResult(false, null, $"Unsupported channel {delivery.Channel}."),
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new SendResult(false, null, ex.Message);
            }

            if (result.Success)
            {
                delivery.MarkSent(result.ProviderMessageId, clock.UtcNow);
            }
            else
            {
                delivery.MarkFailed(result.Error ?? "Unknown error", clock.UtcNow);
                logger.LogWarning("Delivery {Id} on {Channel} failed (attempt {Attempts}): {Error}",
                    delivery.Id, delivery.Channel, delivery.Attempts, result.Error);
            }
        }

        await db.SaveChangesAsync(ct);
        return deliveries.Count;
    }

    private async Task<List<Guid>> ClaimAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand(ClaimSql);
        command.Parameters.Add(new NpgsqlParameter("take", NpgsqlDbType.Integer) { Value = BatchSize });
        command.Parameters.Add(new NpgsqlParameter("stuck", NpgsqlDbType.Integer) { Value = StuckMinutes });

        var ids = new List<Guid>(BatchSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
        return ids;
    }
}
