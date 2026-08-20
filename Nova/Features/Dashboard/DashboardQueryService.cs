using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Dashboard;

/// <summary>
/// Server-side implementation of <see cref="IDashboardQueryService"/>. Composes the authoritative
/// campaign list and join-request surfaces instead of recomputing their counts, and reads the
/// active/archived roster and team counts, the whole-club unresolved placement summary, and the
/// bounded recent-activity feed through the tenant-filtered read context.
/// </summary>
/// <param name="campaignQueryService">The composed campaign list surface.</param>
/// <param name="joinRequestService">The composed join-request surface.</param>
/// <param name="readDbContextFactory">The read-only tenant-scoped context factory.</param>
/// <param name="currentUserProvider">The current user and club context.</param>
/// <param name="logger">The logger for rejected access attempts.</param>
public sealed partial class DashboardQueryService(
    ICampaignQueryService campaignQueryService,
    IClubJoinRequestService joinRequestService,
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<DashboardQueryService> logger) : IDashboardQueryService
{
    private const string UnresolvedActorFallback = "Former member";

    /// <inheritdoc />
    public async Task<ServiceResult<ClubDashboardResult>> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetClubId(out var clubId))
        {
            LogDashboardForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be an approved club member to view the club dashboard.");
        }

        // Compose the active campaign list surface (bounded by the dashboard card cap) in-process and
        // flatten season groups; no new campaign projection is introduced here.
        var listResult = await campaignQueryService.GetCampaignListAsync(
            new GetCampaignListInput { Status = "active", Limit = ClubDashboardResult.ActiveCampaignMaxCount },
            cancellationToken);
        if (listResult.IsProblem)
        {
            return listResult.Problem;
        }

        var cards = listResult.Value.Seasons
            .SelectMany(season => season.Campaigns.Select(campaign => new ActiveCampaignCardDto
            {
                CampaignId = campaign.CampaignId,
                Name = campaign.Name,
                SeasonName = season.Name,
                StartDate = campaign.StartDate,
                PlannedEndDate = campaign.PlannedEndDate,
                Status = campaign.Status,
                ParticipantCount = campaign.ParticipantCount,
                UnresolvedCount = campaign.UnresolvedCount,
                WorkspaceUrl = DashboardEndpoints.CampaignWorkspaceUrl(campaign.CampaignId)
            }))
            .Take(ClubDashboardResult.ActiveCampaignMaxCount)
            .ToList()
            .AsReadOnly();

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var roster = await ReadRosterCountsAsync(db, cancellationToken);
        var teams = await ReadTeamCountsAsync(db, cancellationToken);

        AdminAttentionDto? adminAttention = null;
        if (currentUserProvider.IsClubAdmin)
        {
            var attentionResult = await ComposeAdminAttentionAsync(db, clubId, cancellationToken);
            if (attentionResult.IsProblem)
            {
                return attentionResult.Problem;
            }

            adminAttention = attentionResult.Value;
        }

        return new ClubDashboardResult
        {
            ActiveCampaigns = cards,
            Roster = roster,
            Teams = teams,
            AdminAttention = adminAttention
        };
    }

    /// <inheritdoc />
    public async Task<ServiceResult<DashboardActivityResult>> GetActivityAsync(
        GetDashboardActivityInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (!TryGetClubId(out var clubId))
        {
            LogDashboardActivityForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be an approved club member to view club dashboard activity.");
        }

        var limit = input.Limit ?? GetDashboardActivityInput.DefaultLimit;

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);

        var rows = new List<DashboardActivityEventRow>(limit * 4);
        rows.AddRange(await ReadNoteRowsAsync(db, clubId, limit, cancellationToken));
        rows.AddRange(await ReadTagRowsAsync(db, clubId, limit, cancellationToken));
        rows.AddRange(await ReadPlacementRowsAsync(db, clubId, limit, cancellationToken));
        rows.AddRange(await ReadLifecycleRowsAsync(db, clubId, limit, cancellationToken));

        var merged = DashboardActivityFeedPolicy.OrderAndBound(rows, limit);

        var actorDisplayNames = await ResolveActorDisplayNamesAsync(db, clubId, merged, cancellationToken);

        var events = merged
            .Select(row => new DashboardActivityItemDto
            {
                Kind = row.Kind,
                EventId = row.EventId,
                EventAt = row.EventAt,
                ActorUserId = row.ActorUserId,
                ActorDisplayName = ResolveActorDisplayName(actorDisplayNames, row.ActorUserId),
                CampaignId = row.CampaignId,
                CampaignName = row.CampaignName,
                PlayerCampaignAssignmentId = row.PlayerCampaignAssignmentId,
                PlayerDisplayName = row.PlayerDisplayName,
                TagName = row.TagName,
                PlacementOutcome = row.PlacementOutcome,
                LifecycleEventType = row.LifecycleEventType
            })
            .ToList()
            .AsReadOnly();

        return new DashboardActivityResult(events);
    }

    /// <summary>
    /// Composes the administrator-only attention counts: the pending join-request count from the
    /// join-request surface and the whole-club unresolved placement summary (total undecided count
    /// plus first unresolved campaign) read from the tenant-filtered read context, independent of the
    /// dashboard active-campaign card cap.
    /// </summary>
    /// <param name="db">The read-only tenant-scoped context.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The attention DTO or a propagated composed problem.</returns>
    private async Task<ServiceResult<AdminAttentionDto>> ComposeAdminAttentionAsync(
        NovaReadDbContext db,
        long clubId,
        CancellationToken cancellationToken)
    {
        var joinRequestsResult = await joinRequestService.GetClubJoinRequestsAsync(clubId, cancellationToken);
        if (joinRequestsResult.IsProblem)
        {
            return joinRequestsResult.Problem;
        }

        var unresolved = await ReadClubUnresolvedPlacementAsync(db, clubId, cancellationToken);

        return new AdminAttentionDto
        {
            PendingJoinRequestCount = joinRequestsResult.Value.Count,
            UnresolvedPlacementCount = unresolved.UnresolvedPlacementCount,
            FirstUnresolvedCampaignId = unresolved.FirstUnresolvedCampaignId
        };
    }

    /// <summary>
    /// Reads the authoritative whole-club unresolved placement summary from the tenant-filtered read
    /// context: the total number of undecided participants across every active campaign (independent
    /// of the dashboard active-campaign card cap) and the first active campaign in campaign-list card
    /// order with an undecided participant.
    /// </summary>
    /// <param name="db">The read-only tenant-scoped context.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The whole-club unresolved placement summary.</returns>
    private static async Task<ClubUnresolvedPlacement> ReadClubUnresolvedPlacementAsync(
        NovaReadDbContext db,
        long clubId,
        CancellationToken cancellationToken)
    {
        var undecidedQuery = db.PlayerCampaignAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ClubId == clubId
                && assignment.Campaign.Status == CampaignStatus.Active
                && assignment.PlacementOutcome == PlacementOutcome.Undecided);

        var unresolvedPlacementCount = await undecidedQuery.CountAsync(cancellationToken);

        long? firstUnresolvedCampaignId = null;
        if (unresolvedPlacementCount > 0)
        {
            firstUnresolvedCampaignId = await undecidedQuery
                .OrderByDescending(assignment => assignment.Campaign.Season.StartDate)
                .ThenByDescending(assignment => assignment.Campaign.SeasonId)
                .ThenBy(assignment => assignment.Campaign.Status)
                .ThenByDescending(assignment => assignment.Campaign.StartDate)
                .ThenByDescending(assignment => assignment.Campaign.EndDate.HasValue)
                .ThenByDescending(assignment => assignment.Campaign.EndDate)
                .ThenBy(assignment => assignment.Campaign.Name)
                .ThenByDescending(assignment => assignment.Campaign.CampaignId)
                .ThenByDescending(assignment => assignment.PlayerCampaignAssignmentId)
                .Select(assignment => (long?)assignment.CampaignId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new ClubUnresolvedPlacement(unresolvedPlacementCount, firstUnresolvedCampaignId);
    }

    /// <summary>
    /// Reads the active and archived player counts grouped by lifecycle status from the tenant-filtered read context.
    /// </summary>
    /// <param name="db">The read-only tenant-scoped context.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The active/archived player counts.</returns>
    private static async Task<RosterCountsDto> ReadRosterCountsAsync(
        NovaReadDbContext db,
        CancellationToken cancellationToken)
    {
        var rows = await db.Players
            .GroupBy(player => player.LifecycleStatus)
            .Select(group => new LifecycleCountRow(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        return new RosterCountsDto
        {
            ActivePlayers = rows.FirstOrDefault(row => row.Status == LifecycleStatus.Active)?.Count ?? 0,
            ArchivedPlayers = rows.FirstOrDefault(row => row.Status == LifecycleStatus.Archived)?.Count ?? 0
        };
    }

    /// <summary>
    /// Reads the active and archived team counts grouped by lifecycle status from the tenant-filtered read context.
    /// </summary>
    /// <param name="db">The read-only tenant-scoped context.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The active/archived team counts.</returns>
    private static async Task<TeamCountsDto> ReadTeamCountsAsync(
        NovaReadDbContext db,
        CancellationToken cancellationToken)
    {
        var rows = await db.Teams
            .GroupBy(team => team.LifecycleStatus)
            .Select(group => new LifecycleCountRow(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        return new TeamCountsDto
        {
            ActiveTeams = rows.FirstOrDefault(row => row.Status == LifecycleStatus.Active)?.Count ?? 0,
            ArchivedTeams = rows.FirstOrDefault(row => row.Status == LifecycleStatus.Archived)?.Count ?? 0
        };
    }

    /// <summary>
    /// Reads the bounded note-added activity rows. PostgreSQL orders and bounds in SQL; SQLite
    /// materializes and applies the identical deterministic order and bound in memory because it
    /// cannot translate <see cref="DateTimeOffset"/> ordering.
    /// </summary>
    private async Task<IReadOnlyList<DashboardActivityEventRow>> ReadNoteRowsAsync(
        NovaReadDbContext db,
        long clubId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.Notes
            .AsNoTracking()
            .Where(note => note.ClubId == clubId);

        if (db.Database.IsNpgsql())
        {
            return await query
                .OrderByDescending(note => note.CreatedAt)
                .ThenByDescending(note => note.NoteId)
                .Take(limit)
                .Select(note => new DashboardActivityEventRow(
                    DashboardActivityEventKind.NoteAdded,
                    note.NoteId,
                    note.CreatedAt,
                    note.CreatedById,
                    note.PlayerCampaignAssignment.CampaignId,
                    note.PlayerCampaignAssignment.Campaign.Name,
                    note.PlayerCampaignAssignmentId,
                    $"{note.PlayerCampaignAssignment.Player.FirstName} {note.PlayerCampaignAssignment.Player.LastName}",
                    null,
                    null,
                    null))
                .ToListAsync(cancellationToken);
        }

        var rows = await query
            .Select(note => new DashboardActivityEventRow(
                DashboardActivityEventKind.NoteAdded,
                note.NoteId,
                note.CreatedAt,
                note.CreatedById,
                note.PlayerCampaignAssignment.CampaignId,
                note.PlayerCampaignAssignment.Campaign.Name,
                note.PlayerCampaignAssignmentId,
                $"{note.PlayerCampaignAssignment.Player.FirstName} {note.PlayerCampaignAssignment.Player.LastName}",
                null,
                null,
                null))
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(row => row.EventAt)
            .ThenByDescending(row => row.EventId)
            .Take(limit)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Reads the bounded tag-applied activity rows with full player and tag context.
    /// </summary>
    private async Task<IReadOnlyList<DashboardActivityEventRow>> ReadTagRowsAsync(
        NovaReadDbContext db,
        long clubId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.CampaignTagApplications
            .AsNoTracking()
            .Where(application => application.ClubId == clubId);

        if (db.Database.IsNpgsql())
        {
            return await query
                .OrderByDescending(application => application.CreatedAt)
                .ThenByDescending(application => application.CampaignTagApplicationId)
                .Take(limit)
                .Select(application => new DashboardActivityEventRow(
                    DashboardActivityEventKind.TagApplied,
                    application.CampaignTagApplicationId,
                    application.CreatedAt,
                    application.CreatedById,
                    application.PlayerCampaignAssignment.CampaignId,
                    application.PlayerCampaignAssignment.Campaign.Name,
                    application.PlayerCampaignAssignmentId,
                    $"{application.PlayerCampaignAssignment.Player.FirstName} {application.PlayerCampaignAssignment.Player.LastName}",
                    application.PlayerTag.Name,
                    null,
                    null))
                .ToListAsync(cancellationToken);
        }

        var rows = await query
            .Select(application => new DashboardActivityEventRow(
                DashboardActivityEventKind.TagApplied,
                application.CampaignTagApplicationId,
                application.CreatedAt,
                application.CreatedById,
                application.PlayerCampaignAssignment.CampaignId,
                application.PlayerCampaignAssignment.Campaign.Name,
                application.PlayerCampaignAssignmentId,
                $"{application.PlayerCampaignAssignment.Player.FirstName} {application.PlayerCampaignAssignment.Player.LastName}",
                application.PlayerTag.Name,
                null,
                null))
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(row => row.EventAt)
            .ThenByDescending(row => row.EventId)
            .Take(limit)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Reads the bounded placement-change activity rows, using each assignment's latest modification
    /// (its <c>ModifiedAt</c>/<c>ModifiedById</c> audit stamps) as the placement event.
    /// </summary>
    private async Task<IReadOnlyList<DashboardActivityEventRow>> ReadPlacementRowsAsync(
        NovaReadDbContext db,
        long clubId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.PlayerCampaignAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ClubId == clubId && assignment.ModifiedAt != null);

        if (db.Database.IsNpgsql())
        {
            return await query
                .OrderByDescending(assignment => assignment.ModifiedAt)
                .ThenByDescending(assignment => assignment.PlayerCampaignAssignmentId)
                .Take(limit)
                .Select(assignment => new DashboardActivityEventRow(
                    DashboardActivityEventKind.PlacementSet,
                    assignment.PlayerCampaignAssignmentId,
                    // The filter guarantees ModifiedAt is set; coalescing to CreatedAt is a defensive
                    // fallback so the projection stays non-null for both providers.
                    assignment.ModifiedAt ?? assignment.CreatedAt,
                    assignment.ModifiedById ?? assignment.CreatedById,
                    assignment.CampaignId,
                    assignment.Campaign.Name,
                    assignment.PlayerCampaignAssignmentId,
                    $"{assignment.Player.FirstName} {assignment.Player.LastName}",
                    null,
                    assignment.PlacementOutcome,
                    null))
                .ToListAsync(cancellationToken);
        }

        var rows = await query
            .Select(assignment => new DashboardActivityEventRow(
                DashboardActivityEventKind.PlacementSet,
                assignment.PlayerCampaignAssignmentId,
                assignment.ModifiedAt ?? assignment.CreatedAt,
                assignment.ModifiedById ?? assignment.CreatedById,
                assignment.CampaignId,
                assignment.Campaign.Name,
                assignment.PlayerCampaignAssignmentId,
                $"{assignment.Player.FirstName} {assignment.Player.LastName}",
                null,
                assignment.PlacementOutcome,
                null))
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(row => row.EventAt)
            .ThenByDescending(row => row.EventId)
            .Take(limit)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Reads the bounded campaign close and reopen lifecycle activity rows.
    /// </summary>
    private async Task<IReadOnlyList<DashboardActivityEventRow>> ReadLifecycleRowsAsync(
        NovaReadDbContext db,
        long clubId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.CampaignLifecycleEvents
            .AsNoTracking()
            .Where(activityEvent => activityEvent.ClubId == clubId);

        if (db.Database.IsNpgsql())
        {
            return await query
                .OrderByDescending(activityEvent => activityEvent.CreatedAt)
                .ThenByDescending(activityEvent => activityEvent.CampaignLifecycleEventId)
                .Take(limit)
                .Select(activityEvent => new DashboardActivityEventRow(
                    activityEvent.EventType == CampaignLifecycleEventType.Closed
                        ? DashboardActivityEventKind.CampaignClosed
                        : DashboardActivityEventKind.CampaignReopened,
                    activityEvent.CampaignLifecycleEventId,
                    activityEvent.CreatedAt,
                    activityEvent.CreatedById,
                    activityEvent.CampaignId,
                    activityEvent.Campaign.Name,
                    null,
                    null,
                    null,
                    null,
                    activityEvent.EventType))
                .ToListAsync(cancellationToken);
        }

        var rows = await query
            .Select(activityEvent => new DashboardActivityEventRow(
                activityEvent.EventType == CampaignLifecycleEventType.Closed
                    ? DashboardActivityEventKind.CampaignClosed
                    : DashboardActivityEventKind.CampaignReopened,
                activityEvent.CampaignLifecycleEventId,
                activityEvent.CreatedAt,
                activityEvent.CreatedById,
                activityEvent.CampaignId,
                activityEvent.Campaign.Name,
                null,
                null,
                null,
                null,
                activityEvent.EventType))
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(row => row.EventAt)
            .ThenByDescending(row => row.EventId)
            .Take(limit)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Batch-resolves actor display names for the bounded merged rows from the club-scoped user set.
    /// </summary>
    /// <param name="db">The read-only tenant-scoped context.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="rows">The bounded merged activity rows.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A lookup from actor user identifier to resolved display name.</returns>
    private static async Task<Dictionary<long, string>> ResolveActorDisplayNamesAsync(
        NovaReadDbContext db,
        long clubId,
        IReadOnlyList<DashboardActivityEventRow> rows,
        CancellationToken cancellationToken)
    {
        var actorUserIds = rows
            .Select(row => row.ActorUserId)
            .Distinct()
            .ToArray();

        if (actorUserIds.Length == 0)
        {
            return new Dictionary<long, string>();
        }

        return await db.Users
            .Where(user => user.ClubId == clubId && actorUserIds.Contains(user.Id))
            .Select(user => new
            {
                user.Id,
                user.FirstName,
                user.LastName
            })
            .ToDictionaryAsync(
                user => user.Id,
                user => $"{user.FirstName} {user.LastName}",
                cancellationToken);
    }

    /// <summary>
    /// Resolves an actor display name, falling back to the stable "Former member" text when the
    /// actor user row is no longer available in the club.
    /// </summary>
    /// <param name="actorDisplayNames">The actor display-name lookup.</param>
    /// <param name="actorUserId">The actor user identifier.</param>
    /// <returns>The resolved display name, or <see cref="UnresolvedActorFallback"/> when unavailable.</returns>
    private static string ResolveActorDisplayName(
        IReadOnlyDictionary<long, string> actorDisplayNames,
        long actorUserId)
        => actorDisplayNames.TryGetValue(actorUserId, out var displayName)
            ? displayName
            : UnresolvedActorFallback;

    /// <summary>
    /// Resolves the approved caller's current club identifier.
    /// </summary>
    /// <param name="clubId">The current club identifier when available.</param>
    /// <returns><see langword="true"/> when both user and club context are present.</returns>
    private bool TryGetClubId(out long clubId)
    {
        if (currentUserProvider.UserId is long && currentUserProvider.ClubId is long currentClubId)
        {
            clubId = currentClubId;
            return true;
        }

        clubId = default;
        return false;
    }

    /// <summary>
    /// Projection of one lifecycle-status count from the grouped roster/team queries.
    /// </summary>
    /// <param name="Status">The lifecycle status.</param>
    /// <param name="Count">The number of rows with that status.</param>
    private sealed record LifecycleCountRow(LifecycleStatus Status, int Count);

    /// <summary>
    /// Projection of the authoritative whole-club unresolved placement summary read from the read context.
    /// </summary>
    /// <param name="UnresolvedPlacementCount">The total number of undecided participants across all active campaigns.</param>
    /// <param name="FirstUnresolvedCampaignId">The first active campaign in card order with an undecided participant, or <see langword="null"/>.</param>
    private sealed record ClubUnresolvedPlacement(int UnresolvedPlacementCount, long? FirstUnresolvedCampaignId);

    /// <summary>
    /// Logs a dashboard summary read rejected because the caller is not an approved club member.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Club dashboard access forbidden for UserId={UserId}.")]
    private partial void LogDashboardForbidden(long userId);

    /// <summary>
    /// Logs a dashboard activity read rejected because the caller is not an approved club member.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Club dashboard activity access forbidden for UserId={UserId}.")]
    private partial void LogDashboardActivityForbidden(long userId);
}
