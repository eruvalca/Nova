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
            result => IsValidRoster(result, expectedPage, expectedPageSize, input.GraduationYear, unresolvedOnly),
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
    /// <returns><see langword="true"/> when the page is structurally valid, bounded, and ordered.</returns>
    private static bool IsValidRoster(
        PagedResult<CampaignPlacementRosterItem> result,
        int expectedPage,
        int expectedPageSize,
        int? expectedGraduationYear,
        bool expectedUnresolvedOnly)
        => result.Items is not null
            && result.Page == expectedPage
            && result.PageSize == expectedPageSize
            && result.TotalCount >= 0
            && result.Items.Count <= result.PageSize
            && result.Items.All(item => IsValidRosterItem(item, expectedGraduationYear, expectedUnresolvedOnly))
            && IsOrdered(result.Items);

    /// <summary>
    /// Verifies that adjacent roster rows follow the server ordering contract: last name ascending,
    /// then first name ascending, then <see cref="CampaignPlacementRosterItem.PlayerCampaignAssignmentId"/>
    /// ascending. Names are compared ordinally; the server orders by the same logical keys.
    /// </summary>
    /// <param name="items">The roster rows to verify.</param>
    /// <returns><see langword="true"/> when the rows are in non-decreasing contract order.</returns>
    private static bool IsOrdered(IReadOnlyList<CampaignPlacementRosterItem> items)
    {
        for (var index = 1; index < items.Count; index++)
        {
            var previous = items[index - 1];
            var current = items[index];

            var lastNameComparison = StringComparer.Ordinal.Compare(previous.LastName, current.LastName);
            if (lastNameComparison > 0)
            {
                return false;
            }

            if (lastNameComparison < 0)
            {
                continue;
            }

            var firstNameComparison = StringComparer.Ordinal.Compare(previous.FirstName, current.FirstName);
            if (firstNameComparison > 0)
            {
                return false;
            }

            if (firstNameComparison < 0)
            {
                continue;
            }

            if (previous.PlayerCampaignAssignmentId > current.PlayerCampaignAssignmentId)
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
