using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignPlacementQueryService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpCampaignPlacementQueryService(HttpClient http) : ICampaignPlacementQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResult<CampaignPlacementRosterItem>>> GetPlacementRosterAsync(
        GetCampaignPlacementRosterInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        var expectedPage = input.Page ?? GetCampaignPlacementRosterInput.DefaultPage;
        var expectedPageSize = input.PageSize ?? GetCampaignPlacementRosterInput.DefaultPageSize;
        var unresolvedOnly = input.UnresolvedOnly == true;

        using var response = await http.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(input),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<PagedResult<CampaignPlacementRosterItem>>(
            "The server returned an invalid campaign placement roster response.",
            result => IsValidRoster(result, expectedPage, expectedPageSize, input.GraduationYear, unresolvedOnly, input.CampaignId),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignPlacementSummaryDto>> GetPlacementSummaryAsync(
        GetCampaignPlacementSummaryInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        using var response = await http.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(input.CampaignId),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignPlacementSummaryDto>(
            "The server returned an invalid campaign placement summary response.",
            IsValidSummary,
            cancellationToken);
    }

    /// <summary>
    /// Validates that a decoded placement roster page matches the requested page, row shape, and
    /// the server's deterministic ordering contract.
    /// </summary>
    /// <param name="result">The decoded roster page.</param>
    /// <param name="expectedPage">The page the client requested.</param>
    /// <param name="expectedPageSize">The page size the client requested.</param>
    /// <param name="expectedGraduationYear">The optional exact graduation-year filter sent to the server.</param>
    /// <param name="expectedUnresolvedOnly">Whether the client requested unresolved rows only.</param>
    /// <param name="campaignId">The campaign whose local decisions were requested.</param>
    /// <returns><see langword="true"/> when the page is structurally valid, bounded, and ordered.</returns>
    private static bool IsValidRoster(
        PagedResult<CampaignPlacementRosterItem> result,
        int expectedPage,
        int expectedPageSize,
        int? expectedGraduationYear,
        bool expectedUnresolvedOnly,
        long campaignId)
        => result.Items is not null
            && result.Page == expectedPage
            && result.PageSize == expectedPageSize
            && result.TotalCount >= 0
            && result.Items.Count <= result.PageSize
            && result.Items.All(item => IsValidRosterItem(item, expectedGraduationYear, expectedUnresolvedOnly))
            && result.Items.All(item => IsValidSavedDecision(item, campaignId))
            && IsOrdered(result.Items);

    /// <summary>
    /// Verifies the portable part of the server ordering contract: when two adjacent rows share
    /// identical last and first names, the <see cref="CampaignPlacementRosterItem.PlayerCampaignAssignmentId"/>
    /// tie-breaker must be non-decreasing. Different names are not compared ordinally because the
    /// database collation — not ordinal comparison — determines how the server orders them; this
    /// mirrors the precedent in <see cref="HttpCampaignQueryService.CompareCampaign"/>.
    /// </summary>
    /// <param name="items">The roster rows to verify.</param>
    /// <returns><see langword="true"/> when every equal-name adjacent pair has a non-decreasing assignment identifier.</returns>
    private static bool IsOrdered(IReadOnlyList<CampaignPlacementRosterItem> items)
    {
        for (var index = 1; index < items.Count; index++)
        {
            var previous = items[index - 1];
            var current = items[index];

            if (string.Equals(previous.LastName, current.LastName, StringComparison.Ordinal)
                && string.Equals(previous.FirstName, current.FirstName, StringComparison.Ordinal)
                && previous.PlayerCampaignAssignmentId > current.PlayerCampaignAssignmentId)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates the structural shape of a single placement roster row.
    /// </summary>
    /// <param name="item">The roster row to validate.</param>
    /// <param name="expectedGraduationYear">The optional exact graduation-year filter sent to the server.</param>
    /// <param name="expectedUnresolvedOnly">Whether the client requested unresolved rows only.</param>
    /// <returns><see langword="true"/> when the row is structurally valid.</returns>
    private static bool IsValidRosterItem(
        CampaignPlacementRosterItem item,
        int? expectedGraduationYear,
        bool expectedUnresolvedOnly)
        => item is not null
            && item.PlayerCampaignAssignmentId > 0
            && item.PlayerId > 0
            && !string.IsNullOrWhiteSpace(item.DisplayName)
            && !string.IsNullOrWhiteSpace(item.FirstName)
            && !string.IsNullOrWhiteSpace(item.LastName)
            && item.GraduationYear > 0
            && item.PlacementOutcome is >= PlacementOutcome.Undecided and <= PlacementOutcome.Withdrawn
            && IsValidPlacementRelationship(item.PlacementOutcome, item.Team)
            && item.ConcurrencyToken != Guid.Empty
            && (expectedGraduationYear is null || item.GraduationYear == expectedGraduationYear)
            && (!expectedUnresolvedOnly || item.PlacementOutcome == PlacementOutcome.Undecided);

    /// <summary>
    /// Validates that a placement outcome is consistent with whether a team is present.
    /// </summary>
    /// <param name="placementOutcome">The placement outcome to check.</param>
    /// <param name="team">The team summary, or <see langword="null"/> when the outcome carries no team.</param>
    /// <returns><see langword="true"/> when the outcome and team combination is consistent.</returns>
    private static bool IsValidPlacementRelationship(PlacementOutcome placementOutcome, CampaignParticipantTeamSummaryDto? team)
    {
        if (placementOutcome == PlacementOutcome.Assigned)
        {
            return team is not null && team.TeamId > 0 && !string.IsNullOrWhiteSpace(team.TeamName);
        }

        if (placementOutcome is PlacementOutcome.Undecided or PlacementOutcome.NotSelected or PlacementOutcome.Withdrawn)
        {
            return team is null;
        }

        return false;
    }

    /// <summary>Checks that an explicit decision agrees with its campaign-local participation row.</summary>
    /// <param name="item">The validated participation row.</param>
    /// <param name="campaignId">The requested source campaign.</param>
    /// <returns>Whether the saved-decision snapshot is consistent with the row.</returns>
    private static bool IsValidSavedDecision(CampaignPlacementRosterItem item, long campaignId)
        => item.PlacementOutcome == PlacementOutcome.Undecided
            ? item.SavedDecision is null
            : item.SavedDecision is { } decision
                && decision.PlayerCampaignAssignmentId == item.PlayerCampaignAssignmentId
                && decision.PlayerId == item.PlayerId
                && decision.CampaignId == campaignId
                && decision.SeasonId > 0
                && decision.SeasonOpeningSequence > 0
                && decision.Outcome == item.PlacementOutcome
                && decision.TeamId == item.Team?.TeamId
                && decision.ConcurrencyToken == item.ConcurrencyToken;

    /// <summary>
    /// Validates that a decoded placement summary carries internally consistent, non-negative counts.
    /// </summary>
    /// <param name="summary">The decoded summary.</param>
    /// <returns><see langword="true"/> when every count is non-negative and the total equals their sum.</returns>
    private static bool IsValidSummary(CampaignPlacementSummaryDto summary)
        => summary is not null
            && summary.AssignedCount >= 0
            && summary.NotSelectedCount >= 0
            && summary.WithdrawnCount >= 0
            && summary.UndecidedCount >= 0
            && summary.TotalCount >= 0
            && summary.TotalCount
                == summary.AssignedCount
                + summary.NotSelectedCount
                + summary.WithdrawnCount
                + summary.UndecidedCount;
}
