using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Campaigns;

/// <summary>
/// Provides tenant-safe, bounded campaign and campaign-creation setup projections.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory.</param>
/// <param name="currentUserProvider">The current user and club context.</param>
/// <param name="logger">The logger for rejected access attempts and read failures.</param>
public sealed partial class CampaignQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignQueryService> logger) : ICampaignQueryService
{
    private const string UnresolvedActorFallback = "Former member";

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

        if (!TryGetClubId(out var clubId))
        {
            LogCampaignListForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be an approved club member to view campaigns.");
        }

        var limit = input.Limit ?? GetCampaignListInput.DefaultLimit;
        var status = input.Status?.Trim().ToLowerInvariant() switch
        {
            "active" => CampaignStatus.Active,
            "draft" => CampaignStatus.Draft,
            "closed" => CampaignStatus.Closed,
            _ => (CampaignStatus?)null
        };

        try
        {
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
                .ThenBy(campaign => campaign.Status == CampaignStatus.Active
                    ? 0
                    : campaign.Status == CampaignStatus.Draft ? 1 : 2)
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
                    SeasonConcurrencyToken = campaign.Season.ConcurrencyToken,
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
                    row.SeasonEndDate,
                    row.SeasonConcurrencyToken
                })
                .Select(group => new CampaignSeasonGroup
                {
                    SeasonId = group.Key.SeasonId,
                    Name = group.Key.SeasonName,
                    StartDate = group.Key.SeasonStartDate,
                    EndDate = group.Key.SeasonEndDate,
                    ConcurrencyToken = group.Key.SeasonConcurrencyToken,
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
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            LogCampaignListReadFailed(exception);
            return ServiceProblem.ServerError("The campaign list is unavailable.");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignDetailResult>> GetCampaignDetailAsync(
        GetCampaignDetailInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (!TryGetClubId(out var clubId))
        {
            LogCampaignDetailForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be an approved club member to view campaign details.");
        }

        try
        {
            await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
            var campaign = await db.Campaigns
                .AsNoTracking()
                .Where(campaign => campaign.ClubId == clubId && campaign.CampaignId == input.CampaignId)
                .Select(campaign => new CampaignDetailProjection
                {
                    CampaignId = campaign.CampaignId,
                    Name = campaign.Name,
                    Status = campaign.Status,
                    StartDate = campaign.StartDate,
                    PlannedEndDate = campaign.EndDate,
                    ParticipantCount = campaign.PlayerAssignments.Count,
                    SeasonId = campaign.SeasonId,
                    SeasonName = campaign.Season.Name,
                    ClosedAt = campaign.ClosedAt,
                    ClosedByUserId = campaign.ClosedById
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (campaign is null)
            {
                return ServiceProblem.NotFound();
            }

            var closedByDisplayName = campaign.ClosedByUserId is long closedByUserId
                ? await ResolveClosedByDisplayNameAsync(db, clubId, closedByUserId, cancellationToken)
                : null;

            return new CampaignDetailResult
            {
                CampaignId = campaign.CampaignId,
                Name = campaign.Name,
                Status = campaign.Status,
                StartDate = campaign.StartDate,
                PlannedEndDate = campaign.PlannedEndDate,
                ParticipantCount = campaign.ParticipantCount,
                SeasonId = campaign.SeasonId,
                SeasonName = campaign.SeasonName,
                ClosedAt = campaign.ClosedAt,
                ClosedByUserId = campaign.ClosedByUserId,
                ClosedByDisplayName = closedByDisplayName
            };
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            LogCampaignDetailReadFailed(exception);
            return ServiceProblem.ServerError("The campaign detail is unavailable.");
        }
    }

    /// <summary>
    /// Resolves the display name of the user who closed a campaign, falling back to the stable
    /// "Former member" text when the actor user row is no longer available in the club.
    /// </summary>
    /// <param name="db">The read-only tenant-scoped context.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="closedByUserId">The closer user identifier.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The resolved closer display name, or <see cref="UnresolvedActorFallback"/> when unavailable.</returns>
    private static async Task<string> ResolveClosedByDisplayNameAsync(
        NovaReadDbContext db,
        long clubId,
        long closedByUserId,
        CancellationToken cancellationToken)
        => await db.Users
            .Where(user => user.ClubId == clubId && user.Id == closedByUserId)
            .Select(user => $"{user.FirstName} {user.LastName}")
            .FirstOrDefaultAsync(cancellationToken) ?? UnresolvedActorFallback;

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignCreationSetupResult>> GetCreationSetupAsync(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetClubId(out var clubId) || !currentUserProvider.IsClubAdmin)
        {
            LogCreationSetupForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to view campaign setup.");
        }

        try
        {
            await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
            var currentSeason = await db.Seasons
                .AsNoTracking()
                .Where(season => season.ClubId == clubId
                    && season.Club.CurrentSeasonId == season.SeasonId)
                .Select(season => new CampaignSeasonChoice
                {
                    SeasonId = season.SeasonId,
                    Name = season.Name,
                    StartDate = season.StartDate,
                    EndDate = season.EndDate
                })
                .SingleOrDefaultAsync(cancellationToken);
            var activePlayerCount = await db.Players
                .CountAsync(player => player.LifecycleStatus == LifecycleStatus.Active, cancellationToken);
            var activeTeamCount = await db.Teams
                .CountAsync(team => team.LifecycleStatus == LifecycleStatus.Active, cancellationToken);

            return new CampaignCreationSetupResult
            {
                CurrentSeason = currentSeason,
                ActivePlayerCount = activePlayerCount,
                ActiveTeamCount = activeTeamCount
            };
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            LogCreationSetupReadFailed(exception);
            return ServiceProblem.ServerError("Campaign creation setup is unavailable.");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignOpeningReadinessResult>> GetOpeningReadinessAsync(
        long campaignId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetClubId(out var clubId) || !currentUserProvider.IsClubAdmin)
        {
            LogOpeningReadinessForbidden(campaignId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to view campaign opening readiness.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var target = await db.Campaigns
            .Where(campaign => campaign.CampaignId == campaignId && campaign.ClubId == clubId)
            .Select(campaign => new { campaign.Status, campaign.SeasonId, campaign.Club.CurrentSeasonId })
            .SingleOrDefaultAsync(cancellationToken);

        if (target is null)
        {
            return ServiceProblem.NotFound();
        }

        if (target.Status != CampaignStatus.Draft)
        {
            return ServiceProblem.Conflict("Only a Draft campaign has opening readiness.");
        }

        if (target.SeasonId != target.CurrentSeasonId)
        {
            return ServiceProblem.Conflict("Only a Draft in the club's current season can be opened.");
        }

        var activePlayerCount = await db.Players
            .CountAsync(player => player.LifecycleStatus == LifecycleStatus.Active, cancellationToken);
        var activeTeamCount = await db.Teams
            .CountAsync(team => team.LifecycleStatus == LifecycleStatus.Active, cancellationToken);
        var blockingCampaign = await db.Campaigns
            .Where(campaign => campaign.CampaignId != campaignId && campaign.Status == CampaignStatus.Active)
            .OrderBy(campaign => campaign.CampaignId)
            .Select(campaign => new BlockingActiveCampaign(campaign.CampaignId, campaign.Name))
            .FirstOrDefaultAsync(cancellationToken);

        var blockers = new List<CampaignOpeningBlocker>();
        if (activePlayerCount == 0)
        {
            blockers.Add(CampaignOpeningBlocker.NoActivePlayers);
        }

        if (blockingCampaign is not null)
        {
            blockers.Add(CampaignOpeningBlocker.AnotherCampaignActive);
        }

        IReadOnlyList<CampaignOpeningWarning> warnings = activeTeamCount == 0
            ? [CampaignOpeningWarning.NoActiveTeams]
            : [];

        return new CampaignOpeningReadinessResult(
            campaignId,
            activePlayerCount,
            activeTeamCount,
            blockers.Count == 0,
            blockers.AsReadOnly(),
            warnings,
            blockingCampaign);
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
    /// Holds the flat SQL projection for one campaign-detail row before closer display-name resolution.
    /// </summary>
    private sealed class CampaignDetailProjection
    {
        /// <summary>Gets the campaign identifier.</summary>
        public required long CampaignId { get; init; }
        /// <summary>Gets the campaign name.</summary>
        public required string Name { get; init; }
        /// <summary>Gets the campaign lifecycle status.</summary>
        public CampaignStatus Status { get; init; }
        /// <summary>Gets the campaign start date.</summary>
        public DateOnly StartDate { get; init; }
        /// <summary>Gets the optional planned end date.</summary>
        public DateOnly? PlannedEndDate { get; init; }
        /// <summary>Gets the persisted participant count.</summary>
        public int ParticipantCount { get; init; }
        /// <summary>Gets the season identifier.</summary>
        public long SeasonId { get; init; }
        /// <summary>Gets the season name.</summary>
        public required string SeasonName { get; init; }
        /// <summary>Gets when the campaign was closed, or null when active.</summary>
        public DateTimeOffset? ClosedAt { get; init; }
        /// <summary>Gets the closer user identifier, or null when active.</summary>
        public long? ClosedByUserId { get; init; }
    }

    /// <summary>
    /// Holds the flat SQL projection used before grouping campaign rows by season.
    /// </summary>
    private sealed class CampaignListProjection
    {
        /// <summary>Gets the campaign identifier.</summary>
        public long CampaignId { get; init; }
        /// <summary>Gets the campaign name.</summary>
        public required string CampaignName { get; init; }
        /// <summary>Gets the campaign start date.</summary>
        public DateOnly CampaignStartDate { get; init; }
        /// <summary>Gets the optional planned end date.</summary>
        public DateOnly? CampaignPlannedEndDate { get; init; }
        /// <summary>Gets the campaign lifecycle status.</summary>
        public CampaignStatus CampaignStatus { get; init; }
        /// <summary>Gets the season identifier.</summary>
        public long SeasonId { get; init; }
        /// <summary>Gets the season name.</summary>
        public required string SeasonName { get; init; }
        /// <summary>Gets the season start date.</summary>
        public DateOnly SeasonStartDate { get; init; }
        /// <summary>Gets the optional season end date.</summary>
        public DateOnly? SeasonEndDate { get; init; }
        /// <summary>Gets the season metadata concurrency token.</summary>
        public Guid SeasonConcurrencyToken { get; init; }
        /// <summary>Gets the persisted participant count.</summary>
        public int ParticipantCount { get; init; }
        /// <summary>Gets the unresolved participant count.</summary>
        public int UnresolvedCount { get; init; }
    }

    /// <summary>
    /// Logs a campaign-list read rejected because the caller is not an approved member.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign list access forbidden for UserId={UserId}.")]
    private partial void LogCampaignListForbidden(long userId);

    /// <summary>
    /// Logs a campaign-detail read rejected because the caller is not an approved member.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign detail access forbidden for UserId={UserId}.")]
    private partial void LogCampaignDetailForbidden(long userId);

    /// <summary>
    /// Logs a creation-setup read rejected because the caller is not an approved member.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign creation setup access forbidden for UserId={UserId}.")]
    private partial void LogCreationSetupForbidden(long userId);

    /// <summary>Logs a campaign-list read failure.</summary>
    /// <param name="exception">The thrown exception.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Campaign list read failed.")]
    private partial void LogCampaignListReadFailed(Exception exception);

    /// <summary>Logs a campaign-detail read failure.</summary>
    /// <param name="exception">The thrown exception.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Campaign detail read failed.")]
    private partial void LogCampaignDetailReadFailed(Exception exception);

    /// <summary>Logs a creation-setup read failure.</summary>
    /// <param name="exception">The thrown exception.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Campaign creation setup read failed.")]
    private partial void LogCreationSetupReadFailed(Exception exception);

    /// <summary>Logs an opening-readiness request rejected because the caller is not a club administrator.</summary>
    /// <param name="campaignId">The requested campaign identifier.</param>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign opening readiness forbidden for CampaignId={CampaignId} by UserId={UserId}.")]
    private partial void LogOpeningReadinessForbidden(long campaignId, long userId);
}
