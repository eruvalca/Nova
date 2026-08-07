using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Campaigns;

/// <summary>
/// Corrects Active campaign metadata with lifecycle-sensitive locking.
/// Rejects corrections for Closed campaigns and never alters roster enrollment.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped context factory used for metadata mutations.</param>
/// <param name="currentUserProvider">The current user and club state used for authorization.</param>
/// <param name="logger">The logger used for metadata correction outcomes.</param>
public sealed partial class CampaignMetadataService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignMetadataService> logger) : ICampaignMetadataService
{
    /// <inheritdoc />
    public async Task<ServiceResult<UpdateCampaignMetadataResult>> UpdateAsync(
        UpdateCampaignMetadataInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            LogCampaignMetadataValidationFailed(input.CampaignId);
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogCampaignMetadataForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden(
                "You must be a club administrator to update campaign metadata.");
        }

        return await ExecuteWithFreshContextAsync(
            db => UpdateCampaignMetadataAsync(db, input, actorUserId, clubId, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs a campaign metadata update inside EF Core's retrying execution strategy with a fresh
    /// tenant context per attempt.
    /// </summary>
    /// <typeparam name="TResult">The result produced by the operation.</typeparam>
    /// <param name="operation">The mutation to execute with a fresh tenant context.</param>
    /// <param name="cancellationToken">A token that cancels strategy setup or the operation.</param>
    /// <returns>The result returned by the successful execution-strategy attempt.</returns>
    private async Task<TResult> ExecuteWithFreshContextAsync<TResult>(
        Func<NovaDbContext, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        await using var executionStrategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = executionStrategyDb.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await operation(db);
        });
    }

    /// <summary>
    /// Executes one transactional campaign metadata update attempt.
    /// </summary>
    /// <param name="db">The fresh tenant context for this execution attempt.</param>
    /// <param name="input">The campaign metadata correction request.</param>
    /// <param name="actorUserId">The authenticated club administrator identifier.</param>
    /// <param name="clubId">The current tenant club identifier.</param>
    /// <param name="cancellationToken">A token that cancels database work.</param>
    /// <returns>The updated metadata result or a ProblemDetails-mappable failure.</returns>
    private async Task<ServiceResult<UpdateCampaignMetadataResult>> UpdateCampaignMetadataAsync(
        NovaDbContext db,
        UpdateCampaignMetadataInput input,
        long actorUserId,
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireCampaignMutationLockAsync(input.CampaignId, cancellationToken);

        var campaign = await db.Campaigns
            .Include(c => c.Season)
            .SingleOrDefaultAsync(c => c.CampaignId == input.CampaignId, cancellationToken);

        if (campaign is null || campaign.ClubId != clubId)
        {
            LogCampaignNotFound(input.CampaignId, clubId);
            return ServiceProblem.NotFound("The campaign was not found.");
        }

        if (campaign.Status == CampaignStatus.Closed)
        {
            LogCampaignMetadataClosedConflict(input.CampaignId);
            return ServiceProblem.Conflict(
                "Metadata cannot be changed while the campaign is closed. Reopen it first.");
        }

        // Resolve the target season (may differ from the campaign's current season)
        SeasonEntity season;
        if (input.SeasonId == campaign.SeasonId)
        {
            season = campaign.Season;
        }
        else
        {
            var targetSeason = await db.Seasons
                .SingleOrDefaultAsync(s => s.SeasonId == input.SeasonId && s.ClubId == clubId, cancellationToken);
            if (targetSeason is null)
            {
                LogSeasonNotFound(input.SeasonId, clubId);
                return ServiceProblem.NotFound("The target season was not found.");
            }

            season = targetSeason;
        }

        // Cross-field date validation against the target season
        var dateErrors = ValidateCampaignDatesAgainstSeason(input, season);
        if (dateErrors.Count > 0)
        {
            LogCampaignDateValidationFailed(input.CampaignId, season.SeasonId);
            return ServiceProblem.Validation(dateErrors);
        }

        // Duplicate name check within the target season, excluding the current campaign
        var isDuplicate = await db.Campaigns.AnyAsync(
            c => c.SeasonId == input.SeasonId
                && c.Name == input.Name
                && c.CampaignId != input.CampaignId,
            cancellationToken);
        if (isDuplicate)
        {
            LogDuplicateCampaignName(clubId, input.SeasonId);
            return ServiceProblem.Conflict(
                "A campaign with that name already exists in the selected season.");
        }

        campaign.Name = input.Name;
        campaign.SeasonId = input.SeasonId;
        campaign.Season = season;
        campaign.StartDate = input.StartDate;
        campaign.EndDate = input.PlannedEndDate;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogCampaignMetadataConcurrencyConflict(input.CampaignId);
            return ServiceProblem.Conflict(
                "The campaign changed. Reload it and try again.");
        }

        LogCampaignMetadataUpdated(input.CampaignId, season.SeasonId, actorUserId);
        return new UpdateCampaignMetadataResult(
            campaign.CampaignId,
            campaign.Name,
            campaign.StartDate,
            campaign.EndDate,
            campaign.Status,
            season.SeasonId,
            season.Name);
    }

    /// <summary>
    /// Validates that campaign dates are contained within the target season's bounds.
    /// </summary>
    /// <param name="input">The campaign metadata correction request.</param>
    /// <param name="season">The target season entity.</param>
    /// <returns>A field-keyed validation dictionary; empty when the dates fit the season.</returns>
    private static Dictionary<string, string[]> ValidateCampaignDatesAgainstSeason(
        UpdateCampaignMetadataInput input,
        SeasonEntity season)
    {
        var errors = new Dictionary<string, string[]>();

        if (input.StartDate < season.StartDate)
        {
            errors[nameof(UpdateCampaignMetadataInput.StartDate)] =
                ["The campaign start date cannot be before the season start date."];
        }

        if (season.EndDate is DateOnly seasonEndDate)
        {
            if (input.StartDate > seasonEndDate)
            {
                errors[nameof(UpdateCampaignMetadataInput.StartDate)] =
                    ["The campaign start date cannot be after the season end date."];
            }

            errors[nameof(UpdateCampaignMetadataInput.PlannedEndDate)] = input.PlannedEndDate switch
            {
                null => ["A campaign in a finite season must have a planned end date."],
                DateOnly campaignEndDate when campaignEndDate > seasonEndDate
                    => ["The planned campaign end date cannot be after the season end date."],
                _ => []
            };

            if (errors[nameof(UpdateCampaignMetadataInput.PlannedEndDate)].Length == 0)
            {
                errors.Remove(nameof(UpdateCampaignMetadataInput.PlannedEndDate));
            }
        }

        return errors;
    }

    /// <summary>Logs structural validation failure before database access.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign metadata update validation failed for CampaignId={CampaignId}.")]
    private partial void LogCampaignMetadataValidationFailed(long campaignId);

    /// <summary>Logs a metadata update rejected because the caller is not a club administrator.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign metadata update forbidden for UserId={UserId}.")]
    private partial void LogCampaignMetadataForbidden(long userId);

    /// <summary>Logs a campaign that is unavailable in the current tenant.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "CampaignId={CampaignId} was not found for ClubId={ClubId}.")]
    private partial void LogCampaignNotFound(long campaignId, long clubId);

    /// <summary>Logs a metadata update rejected because the campaign is Closed.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign metadata update rejected; CampaignId={CampaignId} is closed.")]
    private partial void LogCampaignMetadataClosedConflict(long campaignId);

    /// <summary>Logs a target season that is not visible in the current tenant.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "SeasonId={SeasonId} was not found for ClubId={ClubId}.")]
    private partial void LogSeasonNotFound(long seasonId, long clubId);

    /// <summary>Logs a campaign-to-season date validation failure.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign date validation failed for CampaignId={CampaignId}, SeasonId={SeasonId}.")]
    private partial void LogCampaignDateValidationFailed(long campaignId, long seasonId);

    /// <summary>Logs a duplicate campaign name conflict within a season.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign name conflict for ClubId={ClubId}, SeasonId={SeasonId}.")]
    private partial void LogDuplicateCampaignName(long clubId, long seasonId);

    /// <summary>Logs a concurrency conflict during campaign metadata update.</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Campaign metadata concurrency conflict for CampaignId={CampaignId}.")]
    private partial void LogCampaignMetadataConcurrencyConflict(long campaignId);

    /// <summary>Logs a successful campaign metadata update.</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "CampaignId={CampaignId} metadata updated in SeasonId={SeasonId} by UserId={ActorUserId}.")]
    private partial void LogCampaignMetadataUpdated(long campaignId, long seasonId, long actorUserId);
}
