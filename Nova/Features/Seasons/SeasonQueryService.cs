using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Seasons;

/// <summary>Provides tenant-safe, bounded season list and detail projections.</summary>
/// <param name="dbContextFactory">The read-only context factory.</param>
/// <param name="currentUserProvider">The current user and club state.</param>
public sealed class SeasonQueryService(
    IDbContextFactory<NovaReadDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider) : ISeasonQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<SeasonPageResult>> ListAsync(
        GetSeasonListInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            return ServiceProblem.Validation(validationErrors);
        }

        if (!TryGetMemberClubId(out var clubId))
        {
            return ServiceProblem.Forbidden("You must be an approved club member to view seasons.");
        }

        var page = input.Page ?? GetSeasonListInput.DefaultPage;
        var pageSize = input.PageSize ?? GetSeasonListInput.DefaultPageSize;
        var offset = GetOffset(page, pageSize);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var totalCount = await db.Seasons.CountAsync(cancellationToken);
        var items = await db.Seasons
            .AsNoTracking()
            .OrderByDescending(season => season.Club.CurrentSeasonId == season.SeasonId)
            .ThenByDescending(season => season.StartDate)
            .ThenByDescending(season => season.SeasonId)
            .Skip(offset)
            .Take(pageSize)
            .Select(season => new SeasonSummary
            {
                SeasonId = season.SeasonId,
                Name = season.Name,
                StartDate = season.StartDate,
                EndDate = season.EndDate,
                IsCurrent = season.Club.CurrentSeasonId == season.SeasonId,
                ConcurrencyToken = season.ConcurrencyToken
            })
            .ToListAsync(cancellationToken);

        return new SeasonPageResult
        {
            Items = items.AsReadOnly(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SeasonDetailResult>> GetAsync(
        GetSeasonDetailInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            return ServiceProblem.Validation(validationErrors);
        }

        if (!TryGetMemberClubId(out var clubId))
        {
            return ServiceProblem.Forbidden("You must be an approved club member to view seasons.");
        }

        var page = input.CampaignPage ?? GetSeasonListInput.DefaultPage;
        var pageSize = input.CampaignPageSize ?? GetSeasonListInput.DefaultPageSize;
        var offset = GetOffset(page, pageSize);
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var season = await db.Seasons
            .AsNoTracking()
            .Where(season => season.SeasonId == input.SeasonId)
            .Select(season => new SeasonSummary
            {
                SeasonId = season.SeasonId,
                Name = season.Name,
                StartDate = season.StartDate,
                EndDate = season.EndDate,
                IsCurrent = season.Club.CurrentSeasonId == season.SeasonId,
                ConcurrencyToken = season.ConcurrencyToken
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (season is null)
        {
            return ServiceProblem.NotFound();
        }

        var campaignsQuery = db.Campaigns
            .AsNoTracking()
            .Where(campaign => campaign.SeasonId == input.SeasonId);
        var totalCount = await campaignsQuery.CountAsync(cancellationToken);
        var campaigns = await campaignsQuery
            .OrderByDescending(campaign => campaign.StartDate)
            .ThenByDescending(campaign => campaign.CampaignId)
            .Skip(offset)
            .Take(pageSize)
            .Select(campaign => new SeasonCampaignSummary
            {
                CampaignId = campaign.CampaignId,
                Name = campaign.Name,
                Status = campaign.Status,
                StartDate = campaign.StartDate,
                EndDate = campaign.EndDate,
                ParticipantCount = campaign.PlayerAssignments.Count
            })
            .ToListAsync(cancellationToken);

        return new SeasonDetailResult
        {
            Season = season,
            Campaigns = campaigns.AsReadOnly(),
            CampaignPage = page,
            CampaignPageSize = pageSize,
            CampaignTotalCount = totalCount
        };
    }

    /// <summary>Resolves an approved member's current club identifier.</summary>
    private bool TryGetMemberClubId(out long clubId)
    {
        if (currentUserProvider.UserId is long && currentUserProvider.ClubId is long currentClubId)
        {
            clubId = currentClubId;
            return true;
        }

        clubId = default;
        return false;
    }

    /// <summary>Calculates a provider-safe offset for a structurally valid page request.</summary>
    private static int GetOffset(int page, int pageSize)
        => (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
}
