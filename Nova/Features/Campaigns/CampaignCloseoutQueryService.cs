using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;
using OneOf;

namespace Nova.Features.Campaigns;

/// <summary>
/// Server-side implementation for tenant-safe campaign closeout readiness and bounded recent activity queries.
/// </summary>
/// <param name="readDbContextFactory">The read-only tenant-scoped context factory.</param>
/// <param name="currentUserProvider">The current user provider used for authorization checks.</param>
/// <param name="placementQueryService">The composed placement summary query service.</param>
/// <param name="logger">The logger for expected authorization failures.</param>
public sealed partial class CampaignCloseoutQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ICampaignPlacementQueryService placementQueryService,
    ILogger<CampaignCloseoutQueryService> logger) : ICampaignCloseoutQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<CampaignCloseoutReadinessDto>> GetCloseoutReadinessAsync(
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

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var campaignStatus = await db.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.ClubId == currentClubId && campaign.CampaignId == input.CampaignId)
            .Select(campaign => (CampaignStatus?)campaign.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (campaignStatus is null)
        {
            return ServiceProblem.NotFound();
        }

        // Compose #11's authoritative summary rather than re-deriving counts here.
        var summaryResult = await placementQueryService.GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = input.CampaignId },
            cancellationToken);
        if (summaryResult.IsProblem)
        {
            return summaryResult.Problem;
        }

        var assignmentStates = await db.PlayerCampaignAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ClubId == currentClubId && assignment.CampaignId == input.CampaignId)
            .OrderBy(assignment => assignment.PlayerCampaignAssignmentId)
            .Select(assignment => new CampaignAssignmentClosureState(
                assignment.PlayerCampaignAssignmentId,
                assignment.PlacementOutcome,
                assignment.Player.GraduationYear,
                assignment.TeamId,
                assignment.Team == null ? null : assignment.Team.GraduationYear,
                assignment.Team == null ? null : assignment.Team.LifecycleStatus))
            .ToListAsync(cancellationToken);

        var decision = CampaignClosurePolicy.Evaluate(assignmentStates);
        return decision.Match(
            _ => new CampaignCloseoutReadinessDto(
                input.CampaignId,
                campaignStatus.Value,
                IsReady: true,
                summaryResult.Value,
                Blockers: []),
            blocked => MapBlocked(input.CampaignId, campaignStatus.Value, summaryResult.Value, blocked));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignActivityResult>> GetActivityAsync(
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
        var eventsQuery = db.CampaignLifecycleEvents
            .AsNoTracking()
            .Where(activityEvent => activityEvent.ClubId == currentClubId && activityEvent.CampaignId == input.CampaignId);

        List<ActivityEventRow> eventRows;
        if (db.Database.IsNpgsql())
        {
            eventRows = await eventsQuery
                .OrderByDescending(activityEvent => activityEvent.CreatedAt)
                .ThenByDescending(activityEvent => activityEvent.CampaignLifecycleEventId)
                .Take(limit)
                .Select(activityEvent => new ActivityEventRow(
                    activityEvent.CampaignLifecycleEventId,
                    activityEvent.EventType,
                    activityEvent.CreatedAt,
                    activityEvent.CreatedById))
                .ToListAsync(cancellationToken);
        }
        else
        {
            // SQLite cannot translate DateTimeOffset ORDER BY. Fall back to materializing the
            // campaign's small append-only event rows and applying the identical deterministic
            // ordering and bound in memory; PostgreSQL keeps the SQL-side ordering and bound above.
            var allRows = await eventsQuery
                .Select(activityEvent => new ActivityEventRow(
                    activityEvent.CampaignLifecycleEventId,
                    activityEvent.EventType,
                    activityEvent.CreatedAt,
                    activityEvent.CreatedById))
                .ToListAsync(cancellationToken);
            eventRows = allRows
                .OrderByDescending(row => row.CreatedAt)
                .ThenByDescending(row => row.CampaignLifecycleEventId)
                .Take(limit)
                .ToList();
        }

        var actorUserIds = eventRows
            .Select(row => row.ActorUserId)
            .Distinct()
            .ToArray();
        var actorDisplayNames = actorUserIds.Length == 0
            ? new Dictionary<long, string>()
            : await db.Users
                .Where(user => user.ClubId == currentClubId && actorUserIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    user.FirstName,
                    user.LastName
                })
                .ToDictionaryAsync(
                    row => row.Id,
                    row => $"{row.FirstName} {row.LastName}",
                    cancellationToken);

        var events = eventRows
            .Select(row => new CampaignActivityItemDto(
                row.CampaignLifecycleEventId,
                row.EventType,
                row.CreatedAt,
                row.ActorUserId,
                ResolveActorDisplayName(actorDisplayNames, row.ActorUserId)))
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
    /// Resolves an actor display name from the club-scoped lookup, falling back to empty when the
    /// actor user row is unavailable.
    /// </summary>
    /// <param name="actorDisplayNames">The actor display-name lookup dictionary.</param>
    /// <param name="actorUserId">The actor user identifier.</param>
    /// <returns>The resolved display name, or <see cref="string.Empty"/> when unavailable.</returns>
    private static string ResolveActorDisplayName(IReadOnlyDictionary<long, string> actorDisplayNames, long actorUserId)
        => actorDisplayNames.TryGetValue(actorUserId, out var displayName)
            ? displayName
            : string.Empty;

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
    /// Projection of one bounded activity event row before actor display-name resolution.
    /// </summary>
    /// <param name="CampaignLifecycleEventId">The lifecycle event identifier.</param>
    /// <param name="EventType">The lifecycle transition type.</param>
    /// <param name="CreatedAt">When the transition was recorded.</param>
    /// <param name="ActorUserId">The actor user identifier.</param>
    private sealed record ActivityEventRow(
        long CampaignLifecycleEventId,
        CampaignLifecycleEventType EventType,
        DateTimeOffset CreatedAt,
        long ActorUserId);
}
