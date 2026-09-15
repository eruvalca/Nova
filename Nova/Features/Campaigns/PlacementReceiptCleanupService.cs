using Microsoft.EntityFrameworkCore;
using Nova.Data;

namespace Nova.Features.Campaigns;

/// <summary>Globally removes bounded batches of expired, aggregate-independent placement receipts.</summary>
/// <param name="scopeFactory">Creates an isolated administrative retention scope.</param>
/// <param name="logger">Reports retention failures without affecting capture.</param>
internal sealed partial class PlacementReceiptCleanupService(IServiceScopeFactory scopeFactory, ILogger<PlacementReceiptCleanupService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunPassAsync(stoppingToken);
        }
    }

    /// <summary>Isolates retention failures for retry while allowing shutdown cancellation to propagate.</summary>
    internal async Task RunPassAsync(CancellationToken stoppingToken)
    {
        try
        {
            stoppingToken.ThrowIfCancellationRequested();
            await using var scope = scopeFactory.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<NovaAdminDbContext>>();
            await using var db = await factory.CreateDbContextAsync(stoppingToken);
            await PruneAsync(db, DateTimeOffset.UtcNow, stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            LogCleanupFailed(exception);
        }
    }

    /// <summary>Uses the global expiration index, including receipts whose club no longer exists.</summary>
    internal static async Task PruneAsync(NovaAdminDbContext db, DateTimeOffset now, CancellationToken token)
    {
        if (db.Database.IsNpgsql())
        {
            await db.PlacementMutationReceipts.Where(receipt => receipt.RecoveryExpiresAt <= now)
                .OrderBy(receipt => receipt.RecoveryExpiresAt).ThenBy(receipt => receipt.PlacementMutationReceiptId)
                .Take(500).ExecuteDeleteAsync(token);
        }
        else
        {
            var rows = await db.PlacementMutationReceipts.ToListAsync(token);
            db.PlacementMutationReceipts.RemoveRange(rows.Where(receipt => receipt.RecoveryExpiresAt <= now)
                .OrderBy(receipt => receipt.RecoveryExpiresAt).Take(500));
            await db.SaveChangesAsync(token);
        }
    }

    /// <summary>Records a failed retention pass for the next scheduled retry.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Placement receipt cleanup failed; the next hourly pass will retry.")]
    private partial void LogCleanupFailed(Exception exception);
}
