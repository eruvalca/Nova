using System.Data;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
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
                aggregate = await ReadSqliteAggregateAsync(pendingQuery, cancellationToken);
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
            if (cancellationToken.IsCancellationRequested)
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
    private static async Task<AggregateRow?> ReadSqliteAggregateAsync(
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
    /// Reads the campaigns-needing-placement region. The target is the newest Active campaign with
    /// an unresolved assignment, and the count is participant-level (undecided assignments with no
    /// team and an active player) scoped to that target campaign, so the count and the resolution
    /// target always agree. The region reports <see cref="AttentionRegionStatus.Unavailable"/> on
    /// failure.
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

            var (count, newest) = db.Database.IsNpgsql()
                ? await ReadNpgsqlAggregateAsync(clubId, cancellationToken)
                : await ReadSqliteAggregateAsync(undecidedQuery, cancellationToken);

            return new NeedsPlacementRegion
            {
                Status = AttentionRegionStatus.Loaded,
                Count = count,
                CampaignId = newest?.CampaignId,
                CampaignName = newest?.CampaignName
            };
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            LogNeedsPlacementRegionUnavailable(exception);
            return new NeedsPlacementRegion { Status = AttentionRegionStatus.Unavailable, Count = 0 };
        }
    }

    /// <summary>
    /// Reads the needs-placement aggregate from PostgreSQL inside a repeatable-read snapshot
    /// transaction. The qualifying set is unbounded in principle (one row per undecided
    /// assignment), so the count and the first campaign in deterministic campaign-list order are
    /// projected database-side in two scalar queries rather than materialized; both run under the
    /// same snapshot so a concurrent placement change cannot make the values disagree. The
    /// composite ordering has no single Min/Max aggregate equivalent (season start, season id,
    /// status, campaign start, end-date presence, end date, name, and identifiers), so the
    /// ordered limit-one query is required. A transient failure replays the whole read with a
    /// fresh read context, mirroring the mutation retry convention.
    /// </summary>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The count and the newest campaign row.</returns>
    private async Task<(int Count, CampaignRow? Newest)> ReadNpgsqlAggregateAsync(
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var strategyDb = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyDb.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            (ClubId: clubId, Token: cancellationToken),
            async (state, token) =>
            {
                // The query must be rebuilt against the fresh context so it shares the
                // transaction's snapshot and context lifetime.
                await using var db = await readDbContextFactory.CreateDbContextAsync(token);
                var undecidedQuery = db.PlayerCampaignAssignments
                    .Where(assignment => assignment.ClubId == state.ClubId
                        && assignment.Campaign.Status == CampaignStatus.Active
                        && assignment.PlacementOutcome == PlacementOutcome.Undecided
                        && assignment.TeamId == null
                        && assignment.Player.LifecycleStatus == LifecycleStatus.Active);

                await using var transaction =
                    await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);

                // Select the target Active campaign first (the newest one with an unresolved
                // assignment), then count only that campaign's assignments so the count and the
                // resolution target always agree under one snapshot. One-Active enforcement is
                // deferred to #178, so multiple Active campaigns can coexist for now.
                var newest = await undecidedQuery
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
                    .FirstOrDefaultAsync(token);

                var count = newest is null
                    ? 0
                    : await undecidedQuery
                        .CountAsync(assignment => assignment.CampaignId == newest.CampaignId, token);

                await transaction.CommitAsync(token);
                return (count, newest);
            },
            cancellationToken);
    }

    /// <summary>
    /// Reads the needs-placement aggregate on SQLite, which cannot translate the composite
    /// ORDER BY into a limit-one projection. The undecided set is materialized and aggregated in
    /// memory; in-memory SQLite is bounded (single writer, unit-scoped data), and the
    /// count/newest semantics are the same as the PostgreSQL snapshot aggregate.
    /// </summary>
    /// <param name="undecidedQuery">The undecided-assignment query.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The count and the newest campaign row.</returns>
    private static async Task<(int Count, CampaignRow? Newest)> ReadSqliteAggregateAsync(
        IQueryable<PlayerCampaignAssignmentEntity> undecidedQuery,
        CancellationToken cancellationToken)
    {
        var rows = await undecidedQuery
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
            .ToListAsync(cancellationToken);

        // Count only the newest campaign's unresolved assignments so the count and resolution
        // target agree (mirrors the PostgreSQL aggregate semantics).
        var newest = rows.FirstOrDefault();
        var count = newest is null ? 0 : rows.Count(row => row.CampaignId == newest.CampaignId);
        return (count, newest);
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

    /// <summary>
    /// A campaign row projection.
    /// </summary>
    /// <param name="CampaignId">The campaign identifier.</param>
    /// <param name="CampaignName">The campaign display name.</param>
    private sealed record CampaignRow(long CampaignId, string CampaignName);
}
