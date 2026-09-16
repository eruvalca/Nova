using System.Data;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.SharedKernel.Validation;
using OneOf;

namespace Nova.Features.Campaigns;

/// <summary>
/// Server-side implementation for tenant-safe campaign closeout readiness and bounded recent activity queries.
/// </summary>
/// <param name="readDbContextFactory">The read-only tenant-scoped context factory.</param>
/// <param name="currentUserProvider">The current user provider used for authorization checks.</param>
/// <param name="logger">The logger for expected authorization failures.</param>
internal sealed partial class CampaignCloseoutQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignCloseoutQueryService> logger) : ICampaignCloseoutQueryService
{
    /// <inheritdoc />
#pragma warning disable MA0051 // Keep authorization, bounded database reads, and their result projection together for this query.
    public async Task<ServiceResult<CampaignCloseoutReadinessDto>> GetCloseoutReadinessAsync(
#pragma warning restore MA0051
        GetCampaignCloseoutReadinessInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long currentUserId)
        {
            return ServiceProblem.Forbidden("You must be signed in to view campaign closeout readiness.");
        }

        if (currentUserProvider.ClubId is not long currentClubId)
        {
            LogForbiddenCloseoutAccess(currentUserId, input.CampaignId);
            return ServiceProblem.Forbidden("You do not have permission to view this campaign's closeout readiness.");
        }

        await using var strategyDb = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
        {
            await using var db = await readDbContextFactory.CreateDbContextAsync(token);
            await using var transaction = await db.Database.BeginTransactionAsync(
                db.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, token);
            var result = await ReadReadinessAsync(db, input.CampaignId, currentUserId, currentClubId, token);
            await transaction.CommitAsync(token);
            return result;
        }, cancellationToken);
    }

    private static async Task<ServiceResult<CampaignCloseoutReadinessDto>> ReadReadinessAsync(
        NovaReadDbContext db, long campaignId, long actorId, long clubId, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(user => user.Id == actorId && user.ClubId == clubId, cancellationToken))
        {
            return ServiceProblem.Forbidden("You must currently belong to this club to review closing.");
        }
        var campaign = await db.Campaigns
            .AsNoTracking()
            .Where(candidate => candidate.ClubId == clubId && candidate.CampaignId == campaignId)
            .Select(candidate => new { candidate.Status, candidate.SeasonId, candidate.SeasonOpeningSequence })
            .FirstOrDefaultAsync(cancellationToken);
        if (campaign is null || campaign.Status == CampaignStatus.Draft)
        {
            return ServiceProblem.NotFound();
        }
        var assignmentStates = await db.PlayerCampaignAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ClubId == clubId && assignment.CampaignId == campaignId)
            .OrderBy(assignment => assignment.PlayerCampaignAssignmentId)
            .Select(assignment => new CampaignAssignmentClosureState(
                assignment.PlayerCampaignAssignmentId,
                assignment.PlacementOutcome,
                assignment.Player.GraduationYear,
                assignment.TeamId,
                assignment.Team == null ? null : assignment.Team.GraduationYear,
                assignment.Team == null ? null : assignment.Team.LifecycleStatus))
            .ToListAsync(cancellationToken);

        var summary = Summarize(assignmentStates);
        var decision = CampaignClosurePolicy.Evaluate(assignmentStates);
        var readiness = decision.Match(
            _ => new CampaignCloseoutReadinessDto(
                campaignId,
                campaign.Status,
                IsReady: true,
                summary,
                Blockers: []),
            blocked => MapBlocked(campaignId, campaign.Status, summary, blocked));
        var currentSeasonId = await db.Clubs.Where(club => club.ClubId == clubId)
            .Select(club => club.CurrentSeasonId).SingleOrDefaultAsync(cancellationToken);
        var latest = await db.Campaigns.Where(candidate => candidate.ClubId == clubId
                && candidate.SeasonId == campaign.SeasonId && candidate.SeasonOpeningSequence != null)
            .OrderByDescending(candidate => candidate.SeasonOpeningSequence)
            .Select(candidate => new { candidate.CampaignId, candidate.SeasonOpeningSequence })
            .FirstOrDefaultAsync(cancellationToken);
        var activeId = await db.Campaigns.Where(candidate => candidate.ClubId == clubId
                && candidate.CampaignId != campaignId && candidate.Status == CampaignStatus.Active)
            .Select(candidate => (long?)candidate.CampaignId).FirstOrDefaultAsync(cancellationToken);
        var administrator = await IsAdministratorAsync(db, actorId, cancellationToken);
        var reopen = CampaignReopenPolicy.Evaluate(campaign.Status, campaign.SeasonId, currentSeasonId,
            campaign.SeasonOpeningSequence, latest?.SeasonOpeningSequence, activeId.HasValue);
        var reason = reopen.Match(_ => CampaignReopenUnavailableReason.None, blocked => blocked.Reason);
        return readiness with
        {
            NeedsPlacementCount = await EffectivePlacementQueries.NeedsPlacement(db, clubId)
                .CountAsync(assignment => assignment.CampaignId == campaignId, cancellationToken),
            Lifecycle = new(administrator, administrator && campaign.Status == CampaignStatus.Active && readiness.IsReady,
                administrator && reason == CampaignReopenUnavailableReason.None, reason,
                reason == CampaignReopenUnavailableReason.LaterCampaignOpened ? latest?.CampaignId : activeId)
        };
    }

    private static Task<bool> IsAdministratorAsync(NovaReadDbContext db, long actorId, CancellationToken cancellationToken)
    {
        var administratorRole = Roles.ClubAdmin.ToUpperInvariant();
        return db.UserRoles.AnyAsync(role => role.UserId == actorId
            && db.Roles.Any(definition => definition.Id == role.RoleId && definition.NormalizedName == administratorRole), cancellationToken);
    }

    /// <summary>Totals and blockers use the same local facts, never a separately queried roster page.</summary>
    private static CampaignPlacementSummaryDto Summarize(List<CampaignAssignmentClosureState> states)
        => new(states.Count(state => state.Outcome == PlacementOutcome.Assigned),
            states.Count(state => state.Outcome == PlacementOutcome.NotSelected),
            states.Count(state => state.Outcome == PlacementOutcome.Withdrawn),
            states.Count(state => state.Outcome == PlacementOutcome.Undecided), states.Count);

    /// <inheritdoc />
#pragma warning disable MA0051 // Keep authorization, bounded database reads, and their result projection together for this query.
    public async Task<ServiceResult<CampaignActivityResult>> GetActivityAsync(
#pragma warning restore MA0051
        GetCampaignActivityInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long currentUserId)
        {
            return ServiceProblem.Forbidden("You must be signed in to view campaign activity.");
        }

        if (currentUserProvider.ClubId is not long currentClubId)
        {
            LogForbiddenActivityAccess(currentUserId, input.CampaignId);
            return ServiceProblem.Forbidden("You do not have permission to view this campaign's activity.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var campaignExists = await db.Campaigns
            .AsNoTracking()
            .AnyAsync(campaign => campaign.ClubId == currentClubId && campaign.CampaignId == input.CampaignId, cancellationToken);
        if (!campaignExists)
        {
            return ServiceProblem.NotFound();
        }

        var limit = input.Limit ?? GetCampaignActivityInput.DefaultLimit;
        var eventsQuery = db.ActivityEvents
            .AsNoTracking()
            .Where(activityEvent => activityEvent.ClubId == currentClubId
                && activityEvent.CampaignId == input.CampaignId
                && (activityEvent.EventKind == ActivityEventKind.CampaignClosed
                    || activityEvent.EventKind == ActivityEventKind.CampaignReopened));

        List<ActivityEventRow> eventRows;
        if (db.Database.IsNpgsql())
        {
            eventRows = await eventsQuery
                .OrderByDescending(activityEvent => activityEvent.CreatedAt)
                .ThenByDescending(activityEvent => activityEvent.ActivityEventId)
                .Take(limit)
                .Select(activityEvent => new ActivityEventRow(
                    activityEvent.ActivityEventId,
                    activityEvent.EventKind,
                    activityEvent.CreatedAt,
                    activityEvent.ActorUserId,
                    activityEvent.ActorDisplayName))
                .ToListAsync(cancellationToken);
        }
        else
        {
            // SQLite cannot translate DateTimeOffset ORDER BY. Fall back to materializing the
            // campaign's small append-only event rows and applying the identical deterministic
            // ordering and bound in memory; PostgreSQL keeps the SQL-side ordering and bound above.
            var allRows = await eventsQuery
                .Select(activityEvent => new ActivityEventRow(
                    activityEvent.ActivityEventId,
                    activityEvent.EventKind,
                    activityEvent.CreatedAt,
                    activityEvent.ActorUserId,
                    activityEvent.ActorDisplayName))
                .ToListAsync(cancellationToken);
            eventRows = allRows
                .OrderByDescending(row => row.CreatedAt)
                .ThenByDescending(row => row.ActivityEventId)
                .Take(limit)
                .ToList();
        }

        var events = eventRows
            .Select(row => new CampaignActivityItemDto(
                row.ActivityEventId,
                row.EventKind == ActivityEventKind.CampaignClosed
                    ? CampaignLifecycleEventType.Closed
                    : CampaignLifecycleEventType.Reopened,
                row.CreatedAt,
                row.ActorUserId,
                row.ActorDisplayName))
            .ToList()
            .AsReadOnly();

        return new CampaignActivityResult(events);
    }

    /// <summary>
    /// Maps a blocked policy verdict to a closeout-readiness DTO, emitting one blocker per shared
    /// condition key in stable order and attaching the matching foundation id collection.
    /// </summary>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <param name="status">The campaign lifecycle status.</param>
    /// <param name="summary">The composed placement summary.</param>
    /// <param name="blocked">The foundation policy verdict.</param>
    /// <returns>The not-ready closeout readiness DTO.</returns>
    private static CampaignCloseoutReadinessDto MapBlocked(
        long campaignId,
        CampaignStatus status,
        CampaignPlacementSummaryDto summary,
        CampaignCloseBlocked blocked)
    {
        var blockers = new List<CampaignCloseoutBlockerDto>(blocked.Errors.Count);
        AddBlocker(blockers, blocked, CloseoutBlockerConditions.Outcomes, blocked.UndecidedAssignmentIds);
        AddBlocker(blockers, blocked, CloseoutBlockerConditions.Eligibility, blocked.IneligibleAssignmentIds);
        AddBlocker(blockers, blocked, CloseoutBlockerConditions.ArchivedTeams, blocked.ArchivedTeamAssignmentIds);
        return new CampaignCloseoutReadinessDto(campaignId, status, IsReady: false, summary, blockers.AsReadOnly());
    }

    /// <summary>
    /// Adds one condition-keyed blocker when the foundation verdict carries that condition.
    /// </summary>
    /// <param name="blockers">The blocker list being assembled.</param>
    /// <param name="blocked">The foundation policy verdict.</param>
    /// <param name="condition">The shared condition key.</param>
    /// <param name="assignmentIds">The matching foundation id collection.</param>
    private static void AddBlocker(
        List<CampaignCloseoutBlockerDto> blockers,
        CampaignCloseBlocked blocked,
        string condition,
        IReadOnlyList<long> assignmentIds)
    {
        if (blocked.Errors.TryGetValue(condition, out var messages) && messages.Length > 0)
        {
            blockers.Add(new CampaignCloseoutBlockerDto(condition, assignmentIds.Count, assignmentIds, messages[0]));
        }
    }

    /// <summary>
    /// Logs a closeout-readiness read rejected because the caller is not scoped to a club.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    /// <param name="campaignId">The campaign whose closeout readiness was requested.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "User {UserId} attempted to read campaign {CampaignId} closeout readiness without a club scope.")]
    private partial void LogForbiddenCloseoutAccess(long userId, long campaignId);

    /// <summary>
    /// Logs an activity read rejected because the caller is not scoped to a club.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    /// <param name="campaignId">The campaign whose activity was requested.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "User {UserId} attempted to read campaign {CampaignId} activity without a club scope.")]
    private partial void LogForbiddenActivityAccess(long userId, long campaignId);

    /// <summary>
    /// Projection of one bounded activity event row.
    /// </summary>
    /// <param name="ActivityEventId">The activity event identifier.</param>
    /// <param name="EventKind">The stored activity kind.</param>
    /// <param name="CreatedAt">When the transition was recorded.</param>
    /// <param name="ActorUserId">The actor user identifier.</param>
    /// <param name="ActorDisplayName">The actor display-name snapshot.</param>
    private sealed record ActivityEventRow(
        long ActivityEventId,
        ActivityEventKind EventKind,
        DateTimeOffset CreatedAt,
        long ActorUserId,
        string ActorDisplayName);
}
