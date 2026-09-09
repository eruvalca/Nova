using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Seasons;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Client.Services.Campaigns;

/// <summary>Consumes required, validated effective-placement and immutable campaign-record responses.</summary>
internal sealed class HttpEffectivePlacementQueryService(HttpClient http) : IEffectivePlacementQueryService
{
    /// <inheritdoc />
    public Task<ServiceResult<CurrentSeasonRosterResult>> GetCurrentSeasonRosterAsync(
        GetCurrentSeasonRosterInput input, CancellationToken cancellationToken = default)
        => ReadAsync<CurrentSeasonRosterResult>(input, () => SeasonEndpoints.CurrentRosterUrl(input), result => ValidRoster(result, input), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<CampaignEffectivePlacementsResult>> GetCampaignEffectivePlacementsAsync(
        GetCampaignEffectivePlacementsInput input, CancellationToken cancellationToken = default)
        => ReadAsync<CampaignEffectivePlacementsResult>(input, () => CampaignEndpoints.EffectivePlacementsUrl(input), result => ValidWorking(result, input), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<ClosedCampaignRosterResult>> GetClosedCampaignRosterAsync(
        GetClosedCampaignRosterInput input, CancellationToken cancellationToken = default)
        => ReadAsync<ClosedCampaignRosterResult>(input, () => CampaignEndpoints.ClosedRosterUrl(input), result => ValidClosed(result, input), cancellationToken);

    private async Task<ServiceResult<T>> ReadAsync<T>(PlacementPageInput input, Func<string> url,
        Func<T, bool> validate, CancellationToken token)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }
        using var response = await http.GetAsync(new Uri(url(), UriKind.Relative), token);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(token);
        }
        return await response.Content.ReadRequiredJsonAsync<T>("The server returned an invalid placement response.", validate, token);
    }

    private static bool ValidRoster(CurrentSeasonRosterResult result, GetCurrentSeasonRosterInput input)
        => result is not null && ValidPage(result.Roster, input)
            && (result.Season is null ? result.Roster.Items.Count == 0 && result.Roster.TotalCount == 0
                : ValidSeason(result.Season) && result.Roster.Items.All(row => row is not null
                    && ValidPlayer(row.PlayerId, row.FirstName, row.LastName, row.GraduationYear)
                    && ValidSource(row.Source, row.PlayerId, result.Season.SeasonId, allowUnavailableTeam: false)
                    && row.Source.Decision.Outcome == PlacementOutcome.Assigned
                    && (input.TeamId is null || row.Source.Decision.TeamId == input.TeamId)
                    && (input.GraduationYear is null || row.GraduationYear == input.GraduationYear)))
            && Unique(result.Roster.Items.Select(row => row.PlayerId))
            && Ordered(result.Roster.Items.Select(row => new OrderKey(0, row.LastName, row.FirstName, row.PlayerId)));

    private static bool ValidWorking(CampaignEffectivePlacementsResult result, GetCampaignEffectivePlacementsInput input)
        => result is not null && ValidCampaign(result.Campaign, input.CampaignId, CampaignStatus.Active)
            && result.Counts is { NeedsPlacement: >= 0, OptionalReassignment: >= 0, Resolved: >= 0, Unavailable: >= 0 }
            && ValidPage(result.Participants, input)
            && result.Participants.Items.All(row => ValidWorkingRow(row, result.Campaign, input))
            && Unique(result.Participants.Items.Select(row => row.PlayerId))
            && Unique(result.Participants.Items.Select(row => row.PlayerCampaignAssignmentId))
            && Ordered(result.Participants.Items.Select(row => new OrderKey(row.GraduationYear, row.LastName, row.FirstName, row.PlayerId)));

    private static bool ValidWorkingRow(CampaignEffectivePlacementItem row, PlacementCampaignIdentity campaign,
        GetCampaignEffectivePlacementsInput input)
    {
        if (row is null || !ValidPlayer(row.PlayerId, row.FirstName, row.LastName, row.GraduationYear)
            || row.PlayerCampaignAssignmentId <= 0 || row.ConcurrencyToken == Guid.Empty
            || !Enum.IsDefined(row.PlayerLifecycleStatus) || !Enum.IsDefined(row.Eligibility) || !Enum.IsDefined(row.CorrectionReason)
            || row.TryoutNumber is <= 0
            || input.GraduationYear is int year && row.GraduationYear != year)
        {
            return false;
        }
        if (row.LocalDecision is { } local && (!ValidDecision(local, row.PlayerId, campaign.Season.SeasonId, allowUnavailableTeam: true)
            || local.CampaignId != campaign.CampaignId || local.PlayerCampaignAssignmentId != row.PlayerCampaignAssignmentId
            || local.ConcurrencyToken != row.ConcurrencyToken))
        {
            return false;
        }
        if (row.EffectiveDecision is { } source && !ValidSource(source, row.PlayerId, campaign.Season.SeasonId,
            row.CorrectionReason == PlacementCorrectionReason.TeamUnavailable))
        {
            return false;
        }
        if ((row.LocalDecision is not null || row.EffectiveDecision?.Decision.CampaignId == campaign.CampaignId)
            && row.EffectiveDecision?.Decision != row.LocalDecision)
        {
            return false;
        }
        if (row.CorrectionReason != PlacementCorrectionReason.None && row.EffectiveDecision?.Decision.Outcome != PlacementOutcome.Assigned)
        {
            return false;
        }
        if (row.CorrectionReason == PlacementCorrectionReason.TeamUnavailable
            && (row.EffectiveDecision?.Team is not null || row.EffectiveDecision?.Decision.TeamId is not null))
        {
            return false;
        }
        var validTeam = row.EffectiveDecision?.Decision.Outcome == PlacementOutcome.Assigned
            && row.CorrectionReason == PlacementCorrectionReason.None;
        var expectedTeam = validTeam && row.PlayerLifecycleStatus == LifecycleStatus.Active ? row.EffectiveDecision!.Team : null;
        if (row.EffectiveTeam != expectedTeam)
        {
            return false;
        }
        var expectedState = ExpectedEligibility(row, validTeam);
        return row.Eligibility == expectedState
            && (input.TeamId is null || row.EffectiveTeam?.TeamId == input.TeamId)
            && (!Enum.TryParse<EffectivePlacementEligibility>(input.Eligibility, true, out var requested)
                || !Enum.IsDefined(requested) || row.Eligibility == requested);
    }

    private static EffectivePlacementEligibility ExpectedEligibility(CampaignEffectivePlacementItem row, bool validTeam)
    {
        if (row.PlayerLifecycleStatus != LifecycleStatus.Active || row.EffectiveDecision?.Decision.Outcome == PlacementOutcome.Withdrawn)
        {
            return EffectivePlacementEligibility.Unavailable;
        }
        if (validTeam)
        {
            return EffectivePlacementEligibility.OptionalReassignment;
        }
        return row.LocalDecision?.Outcome == PlacementOutcome.NotSelected
            ? EffectivePlacementEligibility.Resolved : EffectivePlacementEligibility.NeedsPlacement;
    }

    private static bool ValidClosed(ClosedCampaignRosterResult result, GetClosedCampaignRosterInput input)
        => result is not null && ValidCampaign(result.Campaign, input.CampaignId, CampaignStatus.Closed)
            && ValidPage(result.Participants, input)
            && result.Participants.Items.All(row => row is not null && row.PlayerCampaignAssignmentId > 0
                && row.TryoutNumber is null or > 0
                && ValidPlayer(row.PlayerId, row.FirstName, row.LastName, row.GraduationYear)
                && ValidSource(row.Source, row.PlayerId, result.Campaign.Season.SeasonId, allowUnavailableTeam: false)
                && row.Source.Decision.CampaignId == input.CampaignId
                && row.Source.Decision.PlayerCampaignAssignmentId == row.PlayerCampaignAssignmentId)
            && Unique(result.Participants.Items.Select(row => row.PlayerId))
            && Unique(result.Participants.Items.Select(row => row.PlayerCampaignAssignmentId))
            && Ordered(result.Participants.Items.Select(row => new OrderKey(0, row.LastName, row.FirstName, row.PlayerId)));

    private static bool ValidPage<T>(PagedResult<T> page, PlacementPageInput input)
        => page is not null && page.Items is not null && page.Items.All(row => row is not null)
            && page.Page == (input.Page ?? 1) && page.PageSize == (input.PageSize ?? PlacementPageInput.DefaultPageSize)
            && page.Items.Count <= page.PageSize && page.TotalCount >= 0;

    private static bool ValidPlayer(long id, string firstName, string lastName, int year)
        => id > 0 && year > 0 && !string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(lastName);
    private static bool ValidSeason(PlacementSeasonIdentity season)
        => season is { SeasonId: > 0 } && !string.IsNullOrWhiteSpace(season.Name);
    private static bool ValidCampaign(PlacementCampaignIdentity campaign, long id, CampaignStatus status)
        => campaign is not null && campaign.CampaignId == id && campaign.Status == status
            && !string.IsNullOrWhiteSpace(campaign.Name) && ValidSeason(campaign.Season);

    private static bool ValidSource(PlacementDecisionSource source, long playerId, long seasonId, bool allowUnavailableTeam)
        => source is not null && !string.IsNullOrWhiteSpace(source.CampaignName)
            && ValidDecision(source.Decision, playerId, seasonId, allowUnavailableTeam)
            && (source.Decision.TeamId is null ? source.Team is null
                : source.Team is { TeamId: > 0 } && source.Team.TeamId == source.Decision.TeamId
                    && !string.IsNullOrWhiteSpace(source.Team.TeamName));

    private static bool ValidDecision(CampaignSavedPlacementDecision decision, long playerId, long seasonId, bool allowUnavailableTeam)
        => decision is not null && decision.PlayerId == playerId && decision.SeasonId == seasonId
            && decision.PlayerCampaignAssignmentId > 0 && decision.CampaignId > 0 && decision.SeasonOpeningSequence > 0
            && decision.RecordedAt != default && decision.RecordedById > 0
            && !string.IsNullOrWhiteSpace(decision.ActorDisplayName) && decision.ConcurrencyToken != Guid.Empty
            && (decision.Outcome == PlacementOutcome.Assigned ? decision.TeamId is > 0 || allowUnavailableTeam && decision.TeamId is null
                : decision.Outcome is PlacementOutcome.NotSelected or PlacementOutcome.Withdrawn && decision.TeamId is null);

    private static bool Unique(IEnumerable<long> ids)
    {
        var seen = new HashSet<long>();
        return ids.All(seen.Add);
    }

    // Database collation determines name order; only numeric keys and exact-name ties are portable.
    private static bool Ordered(IEnumerable<OrderKey> keys)
    {
        OrderKey? previous = null;
        foreach (var key in keys)
        {
            if (previous is not null && (previous.Year > key.Year
                || previous.Year == key.Year && string.Equals(previous.LastName, key.LastName, StringComparison.Ordinal)
                    && string.Equals(previous.FirstName, key.FirstName, StringComparison.Ordinal) && previous.PlayerId >= key.PlayerId))
            {
                return false;
            }
            previous = key;
        }
        return true;
    }

    private sealed record OrderKey(int Year, string LastName, string FirstName, long PlayerId);
}
