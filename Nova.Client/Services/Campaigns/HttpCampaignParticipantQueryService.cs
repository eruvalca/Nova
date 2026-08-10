using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignParticipantQueryService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpCampaignParticipantQueryService(HttpClient http) : ICampaignParticipantQueryService
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

        using var response = await http.GetAsync(
            CampaignEndpoints.GetCampaignParticipantRosterUrl(input),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<PagedResult<CampaignParticipantRosterItem>>(
            "The server returned an invalid campaign participant roster response.",
            result => IsValidRoster(result),
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
            CampaignEndpoints.GetCampaignParticipantDetailUrl(input.CampaignId, input.PlayerCampaignAssignmentId),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignParticipantDetailDto>(
            "The server returned an invalid campaign participant detail response.",
            IsValidDetail,
            cancellationToken);
    }

    private static bool IsValidRoster(PagedResult<CampaignParticipantRosterItem> result)
        => result.Page >= 1
            && result.PageSize >= 1
            && result.PageSize <= GetCampaignParticipantRosterInput.MaxPageSize
            && result.TotalCount >= 0
            && result.Items is not null
            && result.Items.All(item => item is not null && IsValidRosterItem(item));

    private static bool IsValidRosterItem(CampaignParticipantRosterItem item)
        => item.PlayerCampaignAssignmentId > 0
            && item.PlayerId > 0
            && !string.IsNullOrWhiteSpace(item.DisplayName)
            && item.GraduationYear > 0
            && item.PlacementOutcome is >= PlacementOutcome.Undecided and <= PlacementOutcome.Withdrawn
           && (item.Team is null || (item.Team.TeamId > 0 && !string.IsNullOrWhiteSpace(item.Team.TeamName)))
           && item.AppliedTags is not null
           && item.AppliedTags.All(tag => tag.PlayerTagId > 0 && !string.IsNullOrWhiteSpace(tag.TagName));
 
    private static bool IsValidDetail(CampaignParticipantDetailDto detail)
        => detail.PlayerCampaignAssignmentId > 0
            && detail.PlayerId > 0
            && !string.IsNullOrWhiteSpace(detail.DisplayName)
            && detail.GraduationYear > 0
            && detail.PlacementOutcome is >= PlacementOutcome.Undecided and <= PlacementOutcome.Withdrawn
            && detail.Notes is not null
            && detail.Notes.All(note => note.NoteId > 0 && !string.IsNullOrWhiteSpace(note.Content) && !string.IsNullOrWhiteSpace(note.AuthorDisplayName))
            && detail.AppliedTags is not null
            && detail.AppliedTags.All(tag => tag.PlayerTagId > 0 && !string.IsNullOrWhiteSpace(tag.TagName) && !string.IsNullOrWhiteSpace(tag.ActorDisplayName))
            && detail.Capabilities is not null
            && detail.ConcurrencyToken != Guid.Empty
            && detail.CampaignStatus is >= CampaignStatus.Active and <= CampaignStatus.Closed;
}
