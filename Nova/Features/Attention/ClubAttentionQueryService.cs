using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Attention;
using Nova.SharedKernel.Results;

namespace Nova.Features.Attention;

/// <summary>
/// Provides the administrator-only club attention projection. The two regions load in separate
/// context scopes and are individually failure-aware: a transient failure in one region reports
/// <see cref="AttentionRegionStatus.Unavailable"/> without zeroing or hiding the other region.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory.</param>
/// <param name="currentUserProvider">The current user and club context.</param>
/// <param name="logger">The logger for rejected access attempts and region failures.</param>
internal sealed partial class ClubAttentionQueryService(
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
            var pendingQuery = db.ClubJoinRequests
                .Where(request => request.ClubId == clubId && request.Status == RequestStatus.Pending);

            AggregateRow? aggregate;
            if (db.Database.IsNpgsql())
            {
                aggregate = await pendingQuery
                    .GroupBy(_ => 1)
                    .Select(group => new AggregateRow(group.Count(), group.Min(request => request.CreatedAt)))
                    .FirstOrDefaultAsync(cancellationToken);
            }
            else
            {
                aggregate = await ReadSqlitePendingRequestsAggregateAsync(pendingQuery, cancellationToken);
            }

            return new PendingJoinRequestsRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = aggregate?.Count ?? 0,
                OldestRequestAt = aggregate?.OldestRequestAt
            };
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException || cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            LogJoinRequestsRegionUnavailable(exception);
            return new PendingJoinRequestsRegion { Status = AttentionRegionStatus.Unavailable, Count = 0 };
        }
    }

    /// <summary>
    /// Reads the pending join-requests aggregate. SQLite cannot translate ORDER BY on
    /// DateTimeOffset columns, so the rows are materialized and aggregated in memory; pending sets
    /// are small, and the count/oldest semantics are identical to the SQL aggregate.
    /// </summary>
    /// <param name="pendingQuery">The pending join-requests query.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The pending aggregate, or null when no rows qualify.</returns>
    private static async Task<AggregateRow?> ReadSqlitePendingRequestsAggregateAsync(
        IQueryable<ClubJoinRequestEntity> pendingQuery,
        CancellationToken cancellationToken)
    {
        var rows = await pendingQuery
            .Select(request => request.CreatedAt)
            .ToListAsync(cancellationToken);
        return rows.Count == 0
            ? null
            : new AggregateRow(rows.Count, rows.Min());
    }

    /// <summary>
    /// Projects count and resolution target in a single SQL aggregate snapshot. Only the current
    /// season's Active campaign can contribute, and region failures remain independent.
    /// </summary>
    private async Task<NeedsPlacementRegion> ReadNeedsPlacementRegionAsync(
        long clubId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
            var region = await EffectivePlacementQueries.NeedsPlacement(db, clubId)
                .GroupBy(assignment => new { assignment.CampaignId, assignment.Campaign.Name })
                .Select(group => new NeedsPlacementRegion
                {
                    Status = AttentionRegionStatus.Loaded,
                    Count = group.Count(),
                    CampaignId = group.Key.CampaignId,
                    CampaignName = group.Key.Name
                }).SingleOrDefaultAsync(cancellationToken);
            return region ?? new NeedsPlacementRegion { Status = AttentionRegionStatus.Loaded, Count = 0 };
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException || cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            LogNeedsPlacementRegionUnavailable(exception);
            return new NeedsPlacementRegion { Status = AttentionRegionStatus.Unavailable, Count = 0 };
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
    /// A pending join-requests aggregate row projection.
    /// </summary>
    /// <param name="Count">The number of pending requests.</param>
    /// <param name="OldestRequestAt">The oldest pending request timestamp.</param>
    private sealed record AggregateRow(int Count, DateTimeOffset? OldestRequestAt);

}
