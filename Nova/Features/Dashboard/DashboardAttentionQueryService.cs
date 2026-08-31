using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;

namespace Nova.Features.Dashboard;

internal sealed class DashboardAttentionQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<DashboardAttentionQueryService> logger) : IAdminAttentionQueryService
{
    public async Task<ServiceResult<AdminAttentionResult>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is null || currentUserProvider.ClubId is null || !currentUserProvider.IsClubAdmin)
        {
            return ServiceProblem.Forbidden("Club administrator access is required.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var pending = await ReadPendingAsync(db, currentUserProvider.ClubId.Value, cancellationToken);
        var needs = await ReadNeedsAsync(db, cancellationToken);
        return new AdminAttentionResult { PendingJoinRequests = pending, NeedsPlacement = needs };
    }

    private static async Task<PendingJoinRequestAttentionDto> ReadPendingAsync(NovaReadDbContext db, long clubId, CancellationToken ct)
    {
        try
        {
            var query = db.ClubJoinRequests.AsNoTracking().Where(r => r.ClubId == clubId && r.Status == RequestStatus.Pending);
            var count = await query.CountAsync(ct);
            var oldest = count == 0 ? null : await query.OrderBy(r => r.CreatedAt).Select(r => (DateTimeOffset?)r.CreatedAt).FirstOrDefaultAsync(ct);
            return new PendingJoinRequestAttentionDto { State = AttentionProjectionState.Available, Count = count, OldestSubmittedAt = oldest };
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            return new PendingJoinRequestAttentionDto { State = AttentionProjectionState.Unavailable };
        }
    }

    private async Task<NeedsPlacementAttentionDto> ReadNeedsAsync(NovaReadDbContext db, CancellationToken ct)
    {
        try
        {
            var rows = await db.PlayerCampaignAssignments.AsNoTracking()
                .Where(a => a.Campaign.Status == CampaignStatus.Active && a.PlacementOutcome == PlacementOutcome.Undecided && a.TeamId == null)
                .GroupBy(a => new { a.CampaignId, a.Campaign.Name })
                .Select(g => new { g.Key.CampaignId, g.Key.Name, Count = g.Count() })
                .ToListAsync(ct);
            if (rows.Count > 1)
            {
                return new NeedsPlacementAttentionDto { State = AttentionProjectionState.Unavailable };
            }

            var row = rows.SingleOrDefault();
            return new NeedsPlacementAttentionDto { State = AttentionProjectionState.Available, Count = row?.Count ?? 0, CampaignId = row?.CampaignId, CampaignName = row?.Name };
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Unable to read dashboard placement attention projection.");
            return new NeedsPlacementAttentionDto { State = AttentionProjectionState.Unavailable };
        }
    }
}
