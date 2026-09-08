using Microsoft.EntityFrameworkCore;
using SportsBookingService.Api.Data;

namespace SportsBookingService.Api.Jobs;

public static class EventRetentionCleanup
{
    public const int RetentionMonths = 3;

    public static Task<int> DeleteExpiredEventsAsync(AppDbContext db, DateOnly cutoff, CancellationToken ct) =>
        db.Events.Where(e => e.Date < cutoff).ExecuteDeleteAsync(ct);
}

public class EventRetentionCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<EventRetentionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(-EventRetentionCleanup.RetentionMonths);

            var deleted = await EventRetentionCleanup.DeleteExpiredEventsAsync(db, cutoff, ct);

            if (deleted > 0)
                logger.LogInformation("Event retention cleanup deleted {Count} event(s) older than {Cutoff}", deleted, cutoff);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Event retention cleanup run failed");
        }
    }
}
