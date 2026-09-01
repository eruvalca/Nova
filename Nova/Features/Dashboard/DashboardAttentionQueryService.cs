using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;

namespace Nova.Features.Dashboard;

/// <summary>Reads the two administrator attention projections independently.</summary>
internal interface IDashboardAttentionProjectionReader
{
    /// <summary>Reads pending join-request attention for one explicitly authorized club.</summary>
    Task<PendingJoinRequestAttentionDto> ReadPendingAsync(long clubId, CancellationToken cancellationToken);

    /// <summary>Reads unresolved placement attention for one explicitly authorized club.</summary>
    Task<NeedsPlacementAttentionDto> ReadNeedsPlacementAsync(long clubId, CancellationToken cancellationToken);
}

/// <summary>Provider-backed implementation of the independent dashboard attention projections.</summary>
internal sealed partial class DashboardAttentionProjectionReader(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ILogger<DashboardAttentionProjectionReader> logger) : IDashboardAttentionProjectionReader
{
    /// <inheritdoc />
    public async Task<PendingJoinRequestAttentionDto> ReadPendingAsync(
        long clubId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
            var row = await db.ClubJoinRequests
                .AsNoTracking()
                .Where(request => request.ClubId == clubId && request.Status == RequestStatus.Pending)
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Count = group.Count(),
                    OldestSubmittedAt = group.Min(request => (DateTimeOffset?)request.CreatedAt)
                })
                .SingleOrDefaultAsync(cancellationToken);

            return new PendingJoinRequestAttentionDto
            {
                State = AttentionProjectionState.Available,
                Count = row?.Count ?? 0,
                OldestSubmittedAt = row?.OldestSubmittedAt
            };
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            LogPendingProjectionUnavailable(exception, clubId);
            return new PendingJoinRequestAttentionDto { State = AttentionProjectionState.Unavailable };
        }
    }

    /// <inheritdoc />
    public async Task<NeedsPlacementAttentionDto> ReadNeedsPlacementAsync(
        long clubId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
            var rows = await db.PlayerCampaignAssignments
                .AsNoTracking()
                .Where(assignment => assignment.ClubId == clubId
                    && assignment.Campaign.Status == CampaignStatus.Active
                    && assignment.PlacementOutcome == PlacementOutcome.Undecided
                    && assignment.TeamId == null)
                .GroupBy(assignment => new { assignment.CampaignId, assignment.Campaign.Name })
                .Select(group => new
                {
                    group.Key.CampaignId,
                    group.Key.Name,
                    Count = group.Count()
                })
                .ToListAsync(cancellationToken);

            var singleCampaign = rows.Count == 1 ? rows[0] : null;
            return new NeedsPlacementAttentionDto
            {
                State = AttentionProjectionState.Available,
                Count = rows.Sum(row => row.Count),
                CampaignId = singleCampaign?.CampaignId,
                CampaignName = singleCampaign?.Name
            };
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException)
        {
            LogPlacementProjectionUnavailable(exception, clubId);
            return new NeedsPlacementAttentionDto { State = AttentionProjectionState.Unavailable };
        }
    }

    /// <summary>Logs a pending-request projection failure without converting it to zero.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Unable to read pending join-request attention for ClubId={ClubId}.")]
    private partial void LogPendingProjectionUnavailable(Exception exception, long clubId);

    /// <summary>Logs a placement projection failure without converting it to zero.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Unable to read placement attention for ClubId={ClubId}.")]
    private partial void LogPlacementProjectionUnavailable(Exception exception, long clubId);
}

/// <summary>Authorizes and composes administrator attention without coupling projection failures.</summary>
internal sealed class DashboardAttentionQueryService(
    IDashboardAttentionProjectionReader projectionReader,
    ICurrentUserProvider currentUserProvider) : IAdminAttentionQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<AdminAttentionResult>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is null
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            return ServiceProblem.Forbidden("Club administrator access is required.");
        }

        var pendingTask = projectionReader.ReadPendingAsync(clubId, cancellationToken);
        var needsPlacementTask = projectionReader.ReadNeedsPlacementAsync(clubId, cancellationToken);
        await Task.WhenAll(pendingTask, needsPlacementTask);

        return new AdminAttentionResult
        {
            PendingJoinRequests = await pendingTask,
            NeedsPlacement = await needsPlacementTask
        };
    }
}
