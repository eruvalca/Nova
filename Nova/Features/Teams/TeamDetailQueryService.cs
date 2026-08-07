using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;

namespace Nova.Features.Teams;

/// <summary>
/// Provides tenant-safe, read-only team detail and placement projections.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory.</param>
/// <param name="currentUserProvider">The current user and club context.</param>
/// <param name="logger">The logger for rejected access and lookup failures.</param>
public sealed partial class TeamDetailQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<TeamDetailQueryService> logger) : ITeamDetailService
{
    /// <summary>
    /// Returns the team profile, its placement history page, and active-campaign summaries
    /// for the team identified by <paramref name="teamId"/>, scoped to the current club tenant.
    /// Returns <see cref="ServiceProblem.Forbidden"/> when the caller has no club membership,
    /// and <see cref="ServiceProblem.NotFound"/> when the team is not visible in the current tenant.
    /// </summary>
    /// <param name="teamId">The team to retrieve.</param>
    /// <param name="cancellationToken">A token to observe for cooperative cancellation.</param>
    /// <returns>
    /// A <see cref="ServiceResult{T}"/> containing a <see cref="TeamDetailDto"/> on success, or a
    /// <see cref="ServiceProblem"/> on authorization or lookup failure.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <see cref="TeamDetailDto.ActivePlacementImpacts"/> is derived from the truncated placement
    /// history page (capped at <see cref="TeamDetailDto.MaxPlacementHistoryItems"/> rows). It reflects only the
    /// Active-campaign rows that survived the page cut.
    /// </para>
    /// <para>
    /// <see cref="TeamDetailDto.ActivePlacementImpactTotalCount"/> is computed from a separate,
    /// unbounded count query against all assignments for the team. It is intentionally independent
    /// of the page and will correctly reflect the full Active count even when Active rows were
    /// excluded from the page by the truncation limit.
    /// </para>
    /// </remarks>
    public async Task<ServiceResult<TeamDetailDto>> GetTeamDetailAsync(
        long teamId,
        CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long
            || currentUserProvider.ClubId is not long clubId)
        {
            LogTeamDetailForbidden(teamId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be an approved club member to view team details.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var team = await db.Teams
            .Where(candidate => candidate.TeamId == teamId && candidate.ClubId == clubId)
            .Select(candidate => new
            {
                candidate.TeamId,
                candidate.ClubId,
                candidate.Name,
                candidate.GraduationYear,
                candidate.LifecycleStatus
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (team is null)
        {
            LogTeamDetailNotFound(teamId, clubId);
            return ServiceProblem.NotFound();
        }

        var placementQuery = db.PlayerCampaignAssignments
            .Where(assignment => assignment.TeamId == teamId && assignment.ClubId == clubId)
            .Select(assignment => new
            {
                assignment.PlayerCampaignAssignmentId,
                assignment.CampaignId,
                CampaignName = assignment.Campaign.Name,
                CampaignStatus = assignment.Campaign.Status,
                CampaignStartDate = assignment.Campaign.StartDate,
                assignment.PlayerId,
                PlayerDisplayName = assignment.Player.FirstName + " " + assignment.Player.LastName,
                PlayerGraduationYear = assignment.Player.GraduationYear,
                assignment.TryoutNumber,
                assignment.PlacementOutcome
            });

        var placementHistoryTotalCount = await placementQuery.CountAsync(cancellationToken);
        var rows = await placementQuery
            .OrderByDescending(row => row.CampaignStatus == CampaignStatus.Active)
            .ThenByDescending(row => row.CampaignStartDate)
            .ThenByDescending(row => row.CampaignId)
            .ThenBy(row => row.PlayerDisplayName)
            .ThenBy(row => row.PlayerId)
            .Take(TeamDetailDto.MaxPlacementHistoryItems)
            .ToListAsync(cancellationToken);

        var placementHistory = rows
            .Select(row => ToPlacementImpact(
                row.PlayerCampaignAssignmentId,
                row.CampaignId,
                row.CampaignName,
                row.CampaignStatus,
                row.CampaignStartDate,
                row.PlayerId,
                row.PlayerDisplayName,
                row.PlayerGraduationYear,
                row.TryoutNumber,
                row.PlacementOutcome))
            .ToList()
            .AsReadOnly();

        var activePlacementImpacts = placementHistory
            .Where(placement => placement.CampaignStatus == CampaignStatus.Active)
            .ToList()
            .AsReadOnly();

        return new TeamDetailDto(
            team.TeamId,
            team.ClubId,
            team.Name,
            team.GraduationYear,
            team.LifecycleStatus,
            activePlacementImpacts,
            placementHistory)
        {
            ActivePlacementImpactTotalCount = await placementQuery
                .CountAsync(row => row.CampaignStatus == CampaignStatus.Active, cancellationToken),
            PlacementHistoryTotalCount = placementHistoryTotalCount,
            IsPlacementHistoryTruncated =
                placementHistoryTotalCount > TeamDetailDto.MaxPlacementHistoryItems
        };
    }

    /// <summary>
    /// Maps one materialized placement projection to its shared DTO.
    /// </summary>
    /// <param name="playerCampaignAssignmentId">The placement assignment identifier.</param>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <param name="campaignName">The campaign display name.</param>
    /// <param name="campaignStatus">The campaign lifecycle status.</param>
    /// <param name="campaignStartDate">The campaign start date.</param>
    /// <param name="playerId">The assigned player identifier.</param>
    /// <param name="playerDisplayName">The assigned player's display name.</param>
    /// <param name="playerGraduationYear">The assigned player's graduation year.</param>
    /// <param name="tryoutNumber">The campaign-scoped tryout number.</param>
    /// <param name="placementOutcome">The placement outcome.</param>
    /// <returns>The placement detail DTO.</returns>
    private static TeamPlacementImpactDto ToPlacementImpact(
        long playerCampaignAssignmentId,
        long campaignId,
        string campaignName,
        CampaignStatus campaignStatus,
        DateOnly campaignStartDate,
        long playerId,
        string playerDisplayName,
        int playerGraduationYear,
        int? tryoutNumber,
        PlacementOutcome placementOutcome)
        => new(
            playerCampaignAssignmentId,
            campaignId,
            campaignName,
            campaignStatus,
            campaignStartDate,
            playerId,
            playerDisplayName,
            playerGraduationYear,
            tryoutNumber,
            placementOutcome);

    /// <summary>
    /// Logs a team detail request rejected for missing membership.
    /// </summary>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team detail access forbidden for TeamId={TeamId} by UserId={UserId}.")]
    private partial void LogTeamDetailForbidden(long teamId, long userId);

    /// <summary>
    /// Logs a team detail request whose team is not visible in the current tenant.
    /// </summary>
    /// <param name="teamId">The requested team identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team detail target not found for TeamId={TeamId} in ClubId={ClubId}.")]
    private partial void LogTeamDetailNotFound(long teamId, long clubId);
}
