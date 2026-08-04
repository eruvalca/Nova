using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Campaigns;

/// <summary>
/// Provides tenant-safe, bounded campaign and campaign-creation setup projections.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory.</param>
/// <param name="currentUserProvider">The current user and club context.</param>
/// <param name="logger">The logger for rejected access attempts.</param>
public sealed partial class CampaignQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignQueryService> logger) : ICampaignQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<CampaignListResult>> GetCampaignListAsync(
        GetCampaignListInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (!TryGetClubId(out _))
        {
            LogCampaignListForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be an approved club member to view campaigns.");
        }

        var limit = input.Limit ?? GetCampaignListInput.DefaultLimit;
        var status = input.Status?.Trim().ToLowerInvariant() switch
        {
            "active" => CampaignStatus.Active,
            "closed" => CampaignStatus.Closed,
            _ => (CampaignStatus?)null
        };

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Campaigns.AsQueryable();
        if (status is CampaignStatus selectedStatus)
        {
            query = query.Where(campaign => campaign.Status == selectedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(campaign => campaign.Season.StartDate)
            .ThenByDescending(campaign => campaign.SeasonId)
            .ThenBy(campaign => campaign.Status)
            .ThenByDescending(campaign => campaign.StartDate)
            .ThenByDescending(campaign => campaign.EndDate.HasValue)
            .ThenByDescending(campaign => campaign.EndDate)
            .ThenBy(campaign => campaign.Name)
            .ThenByDescending(campaign => campaign.CampaignId)
            .Take(limit)
            .Select(campaign => new CampaignListProjection
            {
                CampaignId = campaign.CampaignId,
                CampaignName = campaign.Name,
                CampaignStartDate = campaign.StartDate,
                CampaignPlannedEndDate = campaign.EndDate,
                CampaignStatus = campaign.Status,
                SeasonId = campaign.SeasonId,
                SeasonName = campaign.Season.Name,
                SeasonStartDate = campaign.Season.StartDate,
                SeasonEndDate = campaign.Season.EndDate,
                ParticipantCount = campaign.PlayerAssignments.Count,
                UnresolvedCount = campaign.PlayerAssignments.Count(
                    assignment => assignment.PlacementOutcome == PlacementOutcome.Undecided)
            })
            .ToListAsync(cancellationToken);

        var seasons = rows
            .GroupBy(row => new
            {
                row.SeasonId,
                row.SeasonName,
                row.SeasonStartDate,
                row.SeasonEndDate
            })
            .Select(group => new CampaignSeasonGroup
            {
                SeasonId = group.Key.SeasonId,
                Name = group.Key.SeasonName,
                StartDate = group.Key.SeasonStartDate,
                EndDate = group.Key.SeasonEndDate,
                Campaigns = group.Select(row => new CampaignListItem
                {
                    CampaignId = row.CampaignId,
                    Name = row.CampaignName,
                    StartDate = row.CampaignStartDate,
                    PlannedEndDate = row.CampaignPlannedEndDate,
                    Status = row.CampaignStatus,
                    ParticipantCount = row.ParticipantCount,
                    UnresolvedCount = row.UnresolvedCount
                }).ToList().AsReadOnly()
            })
            .ToList()
            .AsReadOnly();

        return new CampaignListResult
        {
            TotalCount = totalCount,
            Seasons = seasons
        };
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignCreationSetupResult>> GetCreationSetupAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetClubId(out _))
        {
            LogCreationSetupForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be an approved club member to view campaign setup.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var seasonsQuery = db.Seasons.AsQueryable();
        var totalSeasonCount = await seasonsQuery.CountAsync(cancellationToken);
        var seasons = await seasonsQuery
            .OrderByDescending(season => season.StartDate)
            .ThenByDescending(season => season.SeasonId)
            .Take(CampaignCreationSetupResult.MaxSeasonChoices)
            .Select(season => new CampaignSeasonChoice
            {
                SeasonId = season.SeasonId,
                Name = season.Name,
                StartDate = season.StartDate,
                EndDate = season.EndDate
            })
            .ToListAsync(cancellationToken);
        var activePlayerCount = await db.Players
            .CountAsync(player => player.LifecycleStatus == LifecycleStatus.Active, cancellationToken);
        var activeTeamCount = await db.Teams
            .CountAsync(team => team.LifecycleStatus == LifecycleStatus.Active, cancellationToken);

        return new CampaignCreationSetupResult
        {
            Seasons = seasons.AsReadOnly(),
            TotalSeasonCount = totalSeasonCount,
            ActivePlayerCount = activePlayerCount,
            ActiveTeamCount = activeTeamCount
        };
    }

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

    private sealed class CampaignListProjection
    {
        public long CampaignId { get; init; }
        public required string CampaignName { get; init; }
        public DateOnly CampaignStartDate { get; init; }
        public DateOnly? CampaignPlannedEndDate { get; init; }
        public CampaignStatus CampaignStatus { get; init; }
        public long SeasonId { get; init; }
        public required string SeasonName { get; init; }
        public DateOnly SeasonStartDate { get; init; }
        public DateOnly? SeasonEndDate { get; init; }
        public int ParticipantCount { get; init; }
        public int UnresolvedCount { get; init; }
    }

    /// <summary>
    /// Logs a campaign-list read rejected because the caller is not an approved member.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign list access forbidden for UserId={UserId}.")]
    private partial void LogCampaignListForbidden(long userId);

    /// <summary>
    /// Logs a creation-setup read rejected because the caller is not an approved member.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign creation setup access forbidden for UserId={UserId}.")]
    private partial void LogCreationSetupForbidden(long userId);
}
