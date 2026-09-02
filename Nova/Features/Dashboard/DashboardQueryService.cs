using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;

namespace Nova.Features.Dashboard;

/// <summary>
/// Server-side implementation of <see cref="IDashboardQueryService"/>. Composes the authoritative
/// campaign list surface instead of recomputing its counts, and reads the active/archived roster
/// and team counts through the tenant-filtered read context. The club activity feed and the
/// administrator attention projection live on their own endpoints (<c>IClubActivityQueryService</c>
/// and <c>IClubAttentionQueryService</c>).
/// </summary>
/// <param name="campaignQueryService">The composed campaign list surface.</param>
/// <param name="readDbContextFactory">The read-only tenant-scoped context factory.</param>
/// <param name="currentUserProvider">The current user and club context.</param>
/// <param name="logger">The logger for rejected access attempts.</param>
public sealed partial class DashboardQueryService(
    ICampaignQueryService campaignQueryService,
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<DashboardQueryService> logger) : IDashboardQueryService
{
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

        return new ClubDashboardResult
        {
            ActiveCampaigns = cards,
            Roster = roster,
            Teams = teams
        };
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
    /// Logs a dashboard summary read rejected because the caller is not an approved club member.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Club dashboard access forbidden for UserId={UserId}.")]
    private partial void LogDashboardForbidden(long userId);
}
