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
/// <param name="logger">The logger used for denied and failed season reads.</param>
public sealed partial class SeasonQueryService(
    IDbContextFactory<NovaReadDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<SeasonQueryService> logger) : ISeasonQueryService
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
            LogSeasonListForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be an approved club member to view seasons.");
        }

        var page = input.Page ?? GetSeasonListInput.DefaultPage;
        var pageSize = input.PageSize ?? GetSeasonListInput.DefaultPageSize;
        var offset = GetOffset(page, pageSize);
        try
        {
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
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            LogSeasonListReadFailed(exception);
            return ServiceProblem.ServerError("The season list is unavailable.");
        }
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
            LogSeasonDetailForbidden(currentUserProvider.UserId ?? 0, input.SeasonId);
            return ServiceProblem.Forbidden("You must be an approved club member to view seasons.");
        }

        var page = input.CampaignPage ?? GetSeasonListInput.DefaultPage;
        var pageSize = input.CampaignPageSize ?? GetSeasonListInput.DefaultPageSize;
        var offset = GetOffset(page, pageSize);
        try
        {
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
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            LogSeasonDetailReadFailed(exception);
            return ServiceProblem.ServerError("The season detail is unavailable.");
        }
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

    /// <summary>Logs season-list access rejected because the caller is not a club member.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season list access forbidden for UserId={UserId}.")]
    private partial void LogSeasonListForbidden(long userId);

    /// <summary>Logs season-detail access rejected because the caller is not a club member.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Season detail access forbidden for UserId={UserId}, SeasonId={SeasonId}.")]
    private partial void LogSeasonDetailForbidden(long userId, long seasonId);

    /// <summary>Logs a season-list read failure.</summary>
    /// <param name="exception">The thrown exception.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Season list read failed.")]
    private partial void LogSeasonListReadFailed(Exception exception);

    /// <summary>Logs a season-detail read failure.</summary>
    /// <param name="exception">The thrown exception.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Season detail read failed.")]
    private partial void LogSeasonDetailReadFailed(Exception exception);
}
