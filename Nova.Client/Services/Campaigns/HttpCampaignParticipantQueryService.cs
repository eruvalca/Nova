using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignParticipantQueryService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
internal sealed class HttpCampaignParticipantQueryService(HttpClient http) : ICampaignParticipantQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResult<CampaignParticipantRosterItem>>> GetParticipantRosterAsync(
        GetCampaignParticipantRosterInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        var expectedPage = input.Page ?? GetCampaignParticipantRosterInput.DefaultPage;
        var expectedPageSize = input.PageSize ?? GetCampaignParticipantRosterInput.DefaultPageSize;

        using var response = await http.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(input), UriKind.RelativeOrAbsolute),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<PagedResult<CampaignParticipantRosterItem>>(
            "The server returned an invalid campaign participant roster response.",
            result => IsValidRoster(result, expectedPage, expectedPageSize),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignParticipantDetailDto>> GetParticipantDetailAsync(
        GetCampaignParticipantDetailInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        using var response = await http.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantDetailUrl(input.CampaignId, input.PlayerCampaignAssignmentId), UriKind.RelativeOrAbsolute),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignParticipantDetailDto>(
            "The server returned an invalid campaign participant detail response.",
            detail => IsValidDetail(detail, input.PlayerCampaignAssignmentId),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<int>>> GetRosterGraduationYearsAsync(
        GetCampaignParticipantGraduationYearsInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        using var response = await http.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantGraduationYearsUrl(input.CampaignId), UriKind.RelativeOrAbsolute),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var result = await response.Content.ReadRequiredJsonAsync<List<int>>(
            "The server returned an invalid campaign roster graduation-years response.",
            IsValidGraduationYears,
            cancellationToken);
        return result.Match<ServiceResult<IReadOnlyList<int>>>(
            years => years.AsReadOnly(),
            problem => problem);
    }

    /// <summary>
    /// Validates that a decoded roster page matches the requested page and item shape.
    /// </summary>
    /// <param name="result">The decoded roster page.</param>
    /// <param name="expectedPage">The page the client requested.</param>
    /// <param name="expectedPageSize">The page size the client requested.</param>
    /// <returns><see langword="true"/> when the page is structurally valid and bounded.</returns>
    private static bool IsValidRoster(
        PagedResult<CampaignParticipantRosterItem> result,
        int expectedPage,
        int expectedPageSize)
        => result.Items is not null
            && result.Page == expectedPage
            && result.PageSize == expectedPageSize
            && result.TotalCount >= 0
            && result.Items.Count <= result.PageSize
            && result.Items.All(item => item is not null && IsValidRosterItem(item));

    /// <summary>
    /// Validates the structural shape of a single roster row.
    /// </summary>
    /// <param name="item">The roster row to validate.</param>
    /// <returns><see langword="true"/> when the row is structurally valid.</returns>
    private static bool IsValidRosterItem(CampaignParticipantRosterItem item)
        => item.PlayerCampaignAssignmentId > 0
           && item.PlayerId > 0
           && !string.IsNullOrWhiteSpace(item.DisplayName)
           && item.GraduationYear > 0
           && item.PlacementOutcome is >= PlacementOutcome.Undecided and <= PlacementOutcome.Withdrawn
           && IsValidPlacementRelationship(item.PlacementOutcome, item.Team)
           && item.AppliedTags is not null
           && item.AppliedTags.All(tag => tag is not null && tag.PlayerTagId > 0 && !string.IsNullOrWhiteSpace(tag.TagName) && !string.IsNullOrWhiteSpace(tag.TagColor));

    /// <summary>
    /// Validates that a decoded participant detail matches the requested assignment and its nested collections.
    /// </summary>
    /// <param name="detail">The decoded participant detail.</param>
    /// <param name="expectedPlayerCampaignAssignmentId">The assignment the client requested.</param>
    /// <returns><see langword="true"/> when the detail is structurally valid and ordered.</returns>
    private static bool IsValidDetail(CampaignParticipantDetailDto detail, long expectedPlayerCampaignAssignmentId)
        => detail.PlayerCampaignAssignmentId == expectedPlayerCampaignAssignmentId
           && detail.PlayerId > 0
           && !string.IsNullOrWhiteSpace(detail.DisplayName)
           && detail.GraduationYear > 0
           && detail.PlacementOutcome is >= PlacementOutcome.Undecided and <= PlacementOutcome.Withdrawn
           && detail.CreatedAt != default(DateTimeOffset)
           && (detail.ModifiedAt is null || detail.ModifiedAt >= detail.CreatedAt)
           && detail.Capabilities is not null
           && IsValidPlacementRelationship(detail.PlacementOutcome, detail.Team)
           && detail.ConcurrencyToken != Guid.Empty
           && detail.CampaignStatus is CampaignStatus.Active or CampaignStatus.Draft or CampaignStatus.Closed
;

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
    /// Validates that a decoded graduation-years list is positive and strictly ascending.
    /// The server never truncates the response, so there is deliberately no count bound:
    /// rejecting a longer payload would make valid choices silently vanish on the client.
    /// </summary>
    /// <param name="years">The decoded graduation-years list.</param>
    /// <returns><see langword="true"/> when the list is structurally valid.</returns>
    private static bool IsValidGraduationYears(List<int> years)
        => years is not null
           && years.All(year => year > 0)
           && years.Zip(years.Skip(1)).All(pair => pair.First < pair.Second);
}
