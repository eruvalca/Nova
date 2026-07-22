using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Players;
using Nova.Shared.Results;

namespace Nova.Features.Players;

/// <summary>
/// Provides tenant-safe player detail and campaign-history projections for read scenarios.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory used for bounded projections.</param>
/// <param name="currentUserProvider">The current user provider used for authorization and tenancy context.</param>
/// <param name="logger">The logger used for authorization and lookup failures.</param>
public sealed partial class PlayerDetailQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<PlayerDetailQueryService> logger) : IPlayerDetailService
{
    private const string UnresolvedActorFallback = "Former member";

    /// <inheritdoc />
    public async Task<ServiceResult<PlayerDetailDto>> GetPlayerDetailAsync(long playerId, CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long
            || currentUserProvider.ClubId is not long clubId)
        {
            LogPlayerDetailForbidden(playerId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be an approved club member to view player history.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);

        var player = await db.Players
            .Where(candidate => candidate.PlayerId == playerId)
            .Select(candidate => new
            {
                candidate.PlayerId,
                candidate.FirstName,
                candidate.LastName,
                candidate.DateOfBirth,
                candidate.Gender,
                candidate.GraduationYear,
                candidate.JerseyNumber,
                candidate.LifecycleStatus
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (player is null)
        {
            LogPlayerDetailNotFound(playerId, clubId);
            return ServiceProblem.NotFound();
        }

        var assignments = await db.PlayerCampaignAssignments
            .Where(assignment => assignment.PlayerId == playerId)
            .Select(assignment => new
            {
                assignment.PlayerCampaignAssignmentId,
                assignment.CampaignId,
                CampaignName = assignment.Campaign.Name,
                CampaignStatus = assignment.Campaign.Status,
                CampaignStartDate = assignment.Campaign.StartDate,
                assignment.TryoutNumber,
                assignment.PlacementOutcome,
                assignment.TeamId,
                TeamName = assignment.Team != null ? assignment.Team.Name : null,
                TeamGraduationYear = assignment.Team != null ? assignment.Team.GraduationYear : (int?)null,
                TeamLifecycleStatus = assignment.Team != null ? assignment.Team.LifecycleStatus : (LifecycleStatus?)null
            })
            .ToListAsync(cancellationToken);

        var noteRows = await db.Notes
            .Where(note => note.PlayerCampaignAssignment.PlayerId == playerId)
            .Select(note => new
            {
                note.NoteId,
                note.PlayerCampaignAssignmentId,
                note.Content,
                AuthorUserId = note.CreatedById,
                note.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var tagRows = await db.CampaignTagApplications
            .Where(application => application.PlayerCampaignAssignment.PlayerId == playerId)
            .Select(application => new
            {
                application.CampaignTagApplicationId,
                application.PlayerCampaignAssignmentId,
                application.PlayerTagId,
                TagName = application.PlayerTag.Name,
                TagColor = application.PlayerTag.Color,
                IsTagArchived = application.PlayerTag.LifecycleStatus == LifecycleStatus.Archived,
                ApplyingUserId = application.CreatedById,
                AppliedAt = application.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var actorUserIds = noteRows
            .Select(note => note.AuthorUserId)
            .Concat(tagRows.Select(application => application.ApplyingUserId))
            .Distinct()
            .ToArray();

        var actorDisplayNames = actorUserIds.Length == 0
            ? new Dictionary<long, string>()
            : await db.Users
                .Where(user => user.ClubId == clubId && actorUserIds.Contains(user.Id))
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

        var notesByAssignment = noteRows
            .GroupBy(note => note.PlayerCampaignAssignmentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PlayerEvaluationNoteDto>)group
                    .OrderByDescending(note => note.CreatedAt)
                    .ThenByDescending(note => note.NoteId)
                    .Select(note => new PlayerEvaluationNoteDto(
                        note.NoteId,
                        note.Content,
                        note.AuthorUserId,
                        ResolveActorDisplayName(actorDisplayNames, note.AuthorUserId),
                        note.CreatedAt))
                    .ToList()
                    .AsReadOnly());

        var tagsByAssignment = tagRows
            .GroupBy(application => application.PlayerCampaignAssignmentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PlayerTagApplicationDto>)group
                    .OrderByDescending(application => application.AppliedAt)
                    .ThenByDescending(application => application.CampaignTagApplicationId)
                    .Select(application => new PlayerTagApplicationDto(
                        application.CampaignTagApplicationId,
                        application.PlayerTagId,
                        application.TagName,
                        application.TagColor,
                        application.IsTagArchived,
                        application.ApplyingUserId,
                        ResolveActorDisplayName(actorDisplayNames, application.ApplyingUserId),
                        application.AppliedAt))
                    .ToList()
                    .AsReadOnly());

        var currentTraits = tagRows
            .Join(
                assignments,
                tag => tag.PlayerCampaignAssignmentId,
                assignment => assignment.PlayerCampaignAssignmentId,
                (tag, assignment) => new
                {
                    assignment.CampaignStatus,
                    tag.PlayerTagId,
                    tag.TagName,
                    tag.TagColor
                })
            .Where(row => row.CampaignStatus == CampaignStatus.Active)
            .GroupBy(row => new { row.PlayerTagId, row.TagName, row.TagColor })
            .OrderBy(group => group.Key.TagName, StringComparer.Ordinal)
            .ThenBy(group => group.Key.PlayerTagId)
            .Select(group => new PlayerCurrentTraitDto(
                group.Key.PlayerTagId,
                group.Key.TagName,
                group.Key.TagColor))
            .ToList()
            .AsReadOnly();

        var campaignHistory = assignments
            .OrderByDescending(assignment => assignment.CampaignStartDate)
            .ThenByDescending(assignment => assignment.CampaignId)
            .Select(assignment =>
            {
                PlayerTeamSummaryDto? team = null;
                if (assignment.TeamId is long teamId
                    && assignment.TeamName is not null
                    && assignment.TeamGraduationYear is int graduationYear
                    && assignment.TeamLifecycleStatus is LifecycleStatus lifecycleStatus)
                {
                    team = new PlayerTeamSummaryDto(teamId, assignment.TeamName, graduationYear, lifecycleStatus);
                }

                var assignmentNotes = notesByAssignment.GetValueOrDefault(assignment.PlayerCampaignAssignmentId)
                    ?? Array.Empty<PlayerEvaluationNoteDto>();
                var assignmentTags = tagsByAssignment.GetValueOrDefault(assignment.PlayerCampaignAssignmentId)
                    ?? Array.Empty<PlayerTagApplicationDto>();

                return new PlayerCampaignHistoryDto(
                    assignment.PlayerCampaignAssignmentId,
                    assignment.CampaignId,
                    assignment.CampaignName,
                    assignment.CampaignStatus,
                    assignment.CampaignStartDate,
                    assignment.TryoutNumber,
                    assignment.PlacementOutcome,
                    team,
                    assignmentNotes,
                    assignmentTags);
            })
            .ToList()
            .AsReadOnly();

        return new PlayerDetailDto(
            player.PlayerId,
            player.FirstName,
            player.LastName,
            player.DateOfBirth,
            player.Gender,
            player.GraduationYear,
            player.JerseyNumber,
            player.LifecycleStatus,
            currentTraits,
            campaignHistory);
    }

    /// <summary>
    /// Resolves a display name for the supplied actor identifier, using the configured fallback for deleted or unavailable users.
    /// </summary>
    /// <param name="actorDisplayNames">The actor display-name lookup dictionary.</param>
    /// <param name="actorUserId">The actor user identifier.</param>
    /// <returns>The resolved display name, or the stable fallback text when unavailable.</returns>
    private static string ResolveActorDisplayName(IReadOnlyDictionary<long, string> actorDisplayNames, long actorUserId)
        => actorDisplayNames.TryGetValue(actorUserId, out var displayName)
            ? displayName
            : UnresolvedActorFallback;

    /// <summary>
    /// Logs a player detail request rejected because the caller is not a club member.
    /// </summary>
    /// <param name="playerId">The requested player identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player detail access forbidden for PlayerId={PlayerId} by UserId={UserId}.")]
    private partial void LogPlayerDetailForbidden(long playerId, long userId);

    /// <summary>
    /// Logs a player detail request whose player is not visible in the current tenant.
    /// </summary>
    /// <param name="playerId">The requested player identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player detail target not found for PlayerId={PlayerId} in ClubId={ClubId}.")]
    private partial void LogPlayerDetailNotFound(long playerId, long clubId);
}
