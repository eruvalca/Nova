using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignQueryService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpCampaignQueryService(HttpClient http) : ICampaignQueryService
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

        using var response = await http.GetAsync(
            CampaignEndpoints.GetCampaignListUrl(input.Status, input.Limit, input.Page),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignListResult>(
            "The server returned an invalid campaign list response.",
            result => result.Page == (input.Page ?? 1) && IsValidCampaignList(result, input.Limit),
            cancellationToken);
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

        using var response = await http.GetAsync(
            CampaignEndpoints.GetCampaignDetailUrl(input.CampaignId),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignDetailResult>(
            "The server returned an invalid campaign detail response.",
            IsValidCampaignDetail,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignCreationSetupResult>> GetCreationSetupAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(CampaignEndpoints.GetCreationSetup, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignCreationSetupResult>(
            "The server returned an invalid campaign setup response.",
            IsValidCreationSetup,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignOpeningReadinessResult>> GetOpeningReadinessAsync(
        long campaignId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(
            CampaignEndpoints.GetOpeningReadinessUrl(campaignId),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignOpeningReadinessResult>(
            "The server returned an invalid campaign opening-readiness response.",
            result => IsValidOpeningReadiness(result, campaignId),
            cancellationToken);
    }

    /// <summary>
    /// Validates the complete bounded team preview and opening-readiness blocker/warning relationships.
    /// </summary>
    /// <param name="result">The deserialized readiness payload.</param>
    /// <param name="campaignId">The requested campaign identifier.</param>
    /// <returns><see langword="true"/> when the payload satisfies the shared contract.</returns>
    private static bool IsValidOpeningReadiness(CampaignOpeningReadinessResult result, long campaignId)
    {
        if (result.CampaignId != campaignId
            || result.ActivePlayerCount < 0
            || result.ActiveTeamCount < 0
            || result.ActiveTeams is null
            || result.ActiveTeams.Count != Math.Min(5, result.ActiveTeamCount)
            || result.ActiveTeams.Any(team => team is null || team.TeamId <= 0 || string.IsNullOrWhiteSpace(team.Name))
            || result.ActiveTeams.Select(team => team.TeamId).Distinct().Count() != result.ActiveTeams.Count
            || result.Blockers is null
            || result.Warnings is null
            || result.Blockers.Any(blocker => !Enum.IsDefined(blocker))
            || result.Warnings.Any(warning => !Enum.IsDefined(warning))
            || result.Blockers.Distinct().Count() != result.Blockers.Count
            || result.Warnings.Distinct().Count() != result.Warnings.Count)
        {
            return false;
        }

        var hasPlayerBlocker = result.Blockers.Contains(CampaignOpeningBlocker.NoActivePlayers);
        var hasCampaignBlocker = result.Blockers.Contains(CampaignOpeningBlocker.AnotherCampaignActive);
        var hasTeamWarning = result.Warnings.Contains(CampaignOpeningWarning.NoActiveTeams);
        return result.CanOpen == (result.Blockers.Count == 0)
            && hasPlayerBlocker == (result.ActivePlayerCount == 0)
            && hasTeamWarning == (result.ActiveTeamCount == 0)
            && hasCampaignBlocker == (result.BlockingCampaign is not null)
            && (result.BlockingCampaign is null
                || (result.BlockingCampaign.CampaignId > 0
                    && result.BlockingCampaign.CampaignId != campaignId
                    && !string.IsNullOrWhiteSpace(result.BlockingCampaign.CampaignName)));
    }

    /// <summary>
    /// Validates the structural invariants of a campaign-detail success payload.
    /// </summary>
    /// <param name="result">The deserialized campaign-detail payload.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidCampaignDetail(CampaignDetailResult result)
        => result.CampaignId > 0
            && !string.IsNullOrWhiteSpace(result.Name)
            && result.StartDate != default
            && (result.PlannedEndDate is null || result.PlannedEndDate >= result.StartDate)
            && result.ParticipantCount >= 0
            && result.SeasonId > 0
            && !string.IsNullOrWhiteSpace(result.SeasonName)
            && result.Status is CampaignStatus.Active or CampaignStatus.Draft or CampaignStatus.Closed
            && HasValidClosureFields(result);

    /// <summary>
    /// Validates that the closure fields are consistent with the campaign lifecycle status: a Closed
    /// campaign must carry a closure timestamp, closer identifier, and non-empty closer display name,
    /// while an Active or Draft campaign must carry none of them.
    /// </summary>
    /// <param name="result">The campaign-detail payload.</param>
    /// <returns><see langword="true"/> when the closure fields match the status.</returns>
    private static bool HasValidClosureFields(CampaignDetailResult result)
        => result.Status == CampaignStatus.Closed
            ? result.ClosedAt is not null
                && result.ClosedByUserId is > 0
                && !string.IsNullOrWhiteSpace(result.ClosedByDisplayName)
            : result.ClosedAt is null
                && result.ClosedByUserId is null;

    /// <summary>
    /// Validates the structural and ordering invariants of a campaign-list success payload.
    /// </summary>
    /// <param name="result">The deserialized campaign-list payload.</param>
    /// <param name="requestedLimit">The optional bound requested by the caller.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidCampaignList(CampaignListResult result, int? requestedLimit)
    {
        if (result.Seasons is null
            || result.Seasons.Any(season => season is null
                || season.Campaigns is null
                || season.Campaigns.Any(campaign => campaign is null)))
        {
            return false;
        }

        var rows = result.Seasons.SelectMany(season => season.Campaigns).ToList();
        var limit = requestedLimit ?? GetCampaignListInput.DefaultLimit;
        // Count and bounded rows are separate reads, so a concurrent mutation may make
        // the total briefly lag the returned rows; validate bounds without rejecting that state.
        if (rows.Count > limit || result.TotalCount < 0 || result.Page < 1
            || result.Limit != limit
            || result.CurrentSeasonId is <= 0 || result.DraftActivePlayerCount is < 0
            || (result.DraftActivePlayerCount is null && rows.Any(row => row.Status == CampaignStatus.Draft)))
        {
            return false;
        }

        DateOnly? previousSeasonStart = null;
        long? previousSeasonId = null;
        var seenCurrentSeason = false;
        foreach (var season in result.Seasons)
        {
            if (season.SeasonId <= 0
                || string.IsNullOrWhiteSpace(season.Name)
                || season.StartDate == default
                || season.EndDate < season.StartDate
                || season.ConcurrencyToken == Guid.Empty
                || (season.SeasonId == result.CurrentSeasonId && (seenCurrentSeason || previousSeasonId is not null))
                || (previousSeasonStart is not null && previousSeasonId != result.CurrentSeasonId
                    && (season.StartDate > previousSeasonStart
                        || (season.StartDate == previousSeasonStart && season.SeasonId >= previousSeasonId))))
            {
                return false;
            }

            previousSeasonStart = season.StartDate;
            seenCurrentSeason |= season.SeasonId == result.CurrentSeasonId;
            previousSeasonId = season.SeasonId;
            CampaignListItem? previous = null;
            foreach (var campaign in season.Campaigns)
            {
                if (!IsValidCampaign(campaign)
                    || (previous is not null && CompareCampaign(previous, campaign) > 0))
                {
                    return false;
                }

                previous = campaign;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates one campaign row from a successful response.
    /// </summary>
    /// <param name="campaign">The campaign row.</param>
    /// <returns><see langword="true"/> when all row invariants hold.</returns>
    private static bool IsValidCampaign(CampaignListItem campaign)
        => campaign.CampaignId > 0
            && !string.IsNullOrWhiteSpace(campaign.Name)
            && campaign.StartDate != default
            && (campaign.PlannedEndDate is null || campaign.PlannedEndDate >= campaign.StartDate)
            && campaign.ParticipantCount >= 0
            && campaign.UnresolvedCount >= 0
            && campaign.UnresolvedCount <= campaign.ParticipantCount
            && campaign.Status is CampaignStatus.Active or CampaignStatus.Draft or CampaignStatus.Closed
            && (campaign.Status == CampaignStatus.Closed ? campaign.ClosedAt is not null : campaign.ClosedAt is null);

    /// <summary>
    /// Compares adjacent campaign rows using the portable response-order keys.
    /// </summary>
    /// <param name="left">The preceding row.</param>
    /// <param name="right">The following row.</param>
    /// <returns>A positive value when the rows violate the required order.</returns>
    private static int CompareCampaign(CampaignListItem left, CampaignListItem right)
    {
        var status = GetLifecycleSortRank(left.Status).CompareTo(GetLifecycleSortRank(right.Status));
        if (status != 0)
        {
            return status;
        }

        var start = left.Status == CampaignStatus.Closed
            ? Nullable.Compare(right.ClosedAt, left.ClosedAt)
            : right.StartDate.CompareTo(left.StartDate);
        if (start != 0)
        {
            return start;
        }

        return right.CampaignId.CompareTo(left.CampaignId);
    }

    /// <summary>
    /// Gets the portable lifecycle ordering used by the server: Active, Draft, then Closed.
    /// </summary>
    /// <param name="status">The campaign lifecycle status.</param>
    /// <returns>The lifecycle sort rank.</returns>
    private static int GetLifecycleSortRank(CampaignStatus status)
        => status switch
        {
            CampaignStatus.Active => 0,
            CampaignStatus.Draft => 1,
            CampaignStatus.Closed => 2,
            _ => int.MaxValue
        };

    /// <summary>
    /// Validates the structural, count, bound, and ordering invariants of setup data.
    /// </summary>
    /// <param name="result">The deserialized setup payload.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidCreationSetup(CampaignCreationSetupResult result)
        => result is not null
            && result.ActivePlayerCount >= 0
            && result.ActiveTeamCount >= 0
            && (result.CurrentSeason is null
                || (result.CurrentSeason.SeasonId > 0
                    && !string.IsNullOrWhiteSpace(result.CurrentSeason.Name)
                    && result.CurrentSeason.StartDate != default
                    && (result.CurrentSeason.EndDate is null
                        || result.CurrentSeason.EndDate >= result.CurrentSeason.StartDate)));

}
