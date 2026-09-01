using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Features.Attention;
using Nova.Shared.Results;

namespace Nova.Features.Attention;

/// <summary>
/// Provides the administrator-only club attention projection. The two regions load in separate
/// context scopes and are individually failure-aware: a transient failure in one region reports
/// <see cref="AttentionRegionStatus.Unavailable"/> without zeroing or hiding the other region.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory.</param>
/// <param name="currentUserProvider">The current user and club context.</param>
/// <param name="logger">The logger for rejected access attempts and region failures.</param>
public sealed partial class ClubAttentionQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<ClubAttentionQueryService> logger) : IClubAttentionQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubAttentionResult>> GetClubAttentionAsync(
        CancellationToken cancellationToken = default)
    {
        if (!currentUserProvider.IsClubAdmin
            || currentUserProvider.UserId is not long
            || currentUserProvider.ClubId is not long clubId)
        {
            LogForbiddenAttentionAccess(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("Only club administrators can view the club attention projection.");
        }

        var joinRequests = await ReadPendingJoinRequestsRegionAsync(clubId, cancellationToken);
        var needsPlacement = await ReadNeedsPlacementRegionAsync(clubId, cancellationToken);

        return new ClubAttentionResult
        {
            PendingJoinRequests = joinRequests,
            NeedsPlacement = needsPlacement
        };
    }

    /// <summary>
    /// Reads the pending join-requests region. The tenant filter on <see cref="ClubJoinRequestEntity"/>
    /// requires an admin of the target club, so the club filter is applied as a defensive non-null
    /// assertion and the region reports <see cref="AttentionRegionStatus.Unavailable"/> on failure.
    /// </summary>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The pending join-requests region.</returns>
    private async Task<PendingJoinRequestsRegion> ReadPendingJoinRequestsRegionAsync(
        long clubId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
            // SQLite cannot translate ORDER BY on DateTimeOffset columns, so the pending rows are
            // materialized and ordered in memory. Pending sets are small; the count/oldest
            // semantics are identical to ordering in SQL.
            var pending = await db.ClubJoinRequests
                .Where(request => request.ClubId == clubId && request.Status == RequestStatus.Pending)
                .Select(request => new PendingRequestRow(request.CreatedAt))
                .ToListAsync(cancellationToken);

            var ordered = pending.OrderBy(request => request.CreatedAt).ToList();
            return new PendingJoinRequestsRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = ordered.Count,
                OldestRequestAt = ordered.Count > 0 ? ordered[0].CreatedAt : null
            };
        }
        catch (Exception exception)
        {
            LogJoinRequestsRegionUnavailable(exception);
            return new PendingJoinRequestsRegion { Status = AttentionRegionStatus.Unavailable };
        }
    }

    /// <summary>
    /// Reads the campaigns-needing-placement region. The count is participant-level (number of
    /// undecided assignments in the Active campaign with no team and an active player) and the
    /// identifier/name identify the oldest such campaign in deterministic campaign-list order.
    /// The region reports <see cref="AttentionRegionStatus.Unavailable"/> on failure.
    /// </summary>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The needs-placement region.</returns>
    private async Task<NeedsPlacementRegion> ReadNeedsPlacementRegionAsync(
        long clubId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
            var undecidedQuery = db.PlayerCampaignAssignments
                .Where(assignment => assignment.ClubId == clubId
                    && assignment.Campaign.Status == CampaignStatus.Active
                    && assignment.PlacementOutcome == PlacementOutcome.Undecided
                    && assignment.TeamId == null
                    && assignment.Player.LifecycleStatus == LifecycleStatus.Active);

            var count = await undecidedQuery.CountAsync(cancellationToken);

            long? campaignId = null;
            string? campaignName = null;
            if (count > 0)
            {
                var oldest = await undecidedQuery
                    .OrderByDescending(assignment => assignment.Campaign.Season.StartDate)
                    .ThenByDescending(assignment => assignment.Campaign.SeasonId)
                    .ThenBy(assignment => assignment.Campaign.Status)
                    .ThenByDescending(assignment => assignment.Campaign.StartDate)
                    .ThenByDescending(assignment => assignment.Campaign.EndDate.HasValue)
                    .ThenByDescending(assignment => assignment.Campaign.EndDate)
                    .ThenBy(assignment => assignment.Campaign.Name)
                    .ThenByDescending(assignment => assignment.Campaign.CampaignId)
                    .ThenByDescending(assignment => assignment.PlayerCampaignAssignmentId)
                    .Select(assignment => new CampaignRow(assignment.CampaignId, assignment.Campaign.Name))
                    .FirstOrDefaultAsync(cancellationToken);

                campaignId = oldest?.CampaignId;
                campaignName = oldest?.CampaignName;
            }

            return new NeedsPlacementRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = count,
                CampaignId = campaignId,
                CampaignName = campaignName
            };
        }
        catch (Exception exception)
        {
            LogNeedsPlacementRegionUnavailable(exception);
            return new NeedsPlacementRegion { Status = AttentionRegionStatus.Unavailable };
        }
    }

    /// <summary>
    /// Logs an attempted attention read without an approved club administration.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Club attention access forbidden for UserId={UserId}.")]
    private partial void LogForbiddenAttentionAccess(long userId);

    /// <summary>
    /// Logs a pending-join-requests region load failure.
    /// </summary>
    /// <param name="exception">The thrown exception.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Pending join requests attention region unavailable.")]
    private partial void LogJoinRequestsRegionUnavailable(Exception exception);

    /// <summary>
    /// Logs a needs-placement region load failure.
    /// </summary>
    /// <param name="exception">The thrown exception.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Needs placement attention region unavailable.")]
    private partial void LogNeedsPlacementRegionUnavailable(Exception exception);

    /// <summary>
    /// A pending join request row projection.
    /// </summary>
    /// <param name="CreatedAt">When the request was submitted.</param>
    private sealed record PendingRequestRow(DateTimeOffset CreatedAt);

    /// <summary>
    /// A campaign row projection.
    /// </summary>
    /// <param name="CampaignId">The campaign identifier.</param>
    /// <param name="CampaignName">The campaign display name.</param>
    private sealed record CampaignRow(long CampaignId, string CampaignName);
}
