using Microsoft.EntityFrameworkCore;
using Nova.Data;

namespace Nova.Features.Players;

/// <summary>Globally removes bounded batches of expired, aggregate-independent player creation receipts.</summary>
/// <param name="scopeFactory">Creates an isolated administrative retention scope.</param>
/// <param name="logger">Reports retention failures without affecting capture.</param>
/// <param name="timeProvider">Supplies retention time and the hourly schedule.</param>
internal sealed partial class PlayerCreationReceiptCleanupService(IServiceScopeFactory scopeFactory, ILogger<PlayerCreationReceiptCleanupService> logger, TimeProvider timeProvider) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
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
            await PruneAsync(db, timeProvider.GetUtcNow(), stoppingToken);
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
            await db.PlayerCreationReceipts.Where(receipt => receipt.RecoveryExpiresAt <= now)
                .OrderBy(receipt => receipt.RecoveryExpiresAt).ThenBy(receipt => receipt.PlayerCreationReceiptId)
                .Take(500).ExecuteDeleteAsync(token);
        }
        else
        {
            var rows = await db.PlayerCreationReceipts.ToListAsync(token);
            db.PlayerCreationReceipts.RemoveRange(rows.Where(receipt => receipt.RecoveryExpiresAt <= now)
                .OrderBy(receipt => receipt.RecoveryExpiresAt).Take(500));
            await db.SaveChangesAsync(token);
        }
    }

    /// <summary>Records a failed retention pass for the next scheduled retry.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player creation receipt cleanup failed; the next hourly pass will retry.")]
    private partial void LogCleanupFailed(Exception exception);
}
