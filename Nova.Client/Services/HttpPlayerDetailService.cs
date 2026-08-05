using System.Net.Http.Json;
using Nova.Shared.Players;
using Nova.Shared.Results;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="IPlayerDetailService"/>.
/// </summary>
/// <param name="http">The configured HTTP client.</param>
public sealed class HttpPlayerDetailService(HttpClient http) : IPlayerDetailService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlayerDetailDto>> GetPlayerDetailAsync(long playerId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(PlayerEndpoints.GetDetailUrl(playerId), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<PlayerDetailDto>(
            "The server returned an invalid player detail response.",
            detail => IsValidDetail(detail, playerId),
            cancellationToken);
    }

    /// <summary>
    /// Validates the portable invariants of a player-detail payload.
    /// </summary>
    /// <param name="detail">The player detail to validate.</param>
    /// <param name="expectedPlayerId">The player identifier requested by the caller.</param>
    /// <returns><see langword="true"/> when the detail is structurally valid.</returns>
    private static bool IsValidDetail(PlayerDetailDto detail, long expectedPlayerId)
        => detail is not null
            && detail.PlayerId == expectedPlayerId
            && !string.IsNullOrWhiteSpace(detail.FirstName)
            && !string.IsNullOrWhiteSpace(detail.LastName)
            && detail.CurrentTraits is not null
            && detail.CampaignHistory is not null
            && detail.CurrentTraits.All(trait => trait is not null
                && trait.PlayerTagId > 0
                && !string.IsNullOrWhiteSpace(trait.Name)
                && !string.IsNullOrWhiteSpace(trait.Color))
            && detail.CampaignHistory.All(history => history is not null
                && history.PlayerCampaignAssignmentId > 0
                && history.CampaignId > 0
                && !string.IsNullOrWhiteSpace(history.CampaignName)
                && history.CampaignStatus is Nova.Shared.Enums.CampaignStatus.Active
                    or Nova.Shared.Enums.CampaignStatus.Closed
                && history.CampaignStartDate != default
                && IsValidPlacementRelationship(history)
                && history.Notes is not null
                && history.TagApplications is not null
                && history.Notes.All(IsValidNote)
                && history.TagApplications.All(IsValidTagApplication)
                && AreNotesOrdered(history.Notes)
                && AreTagApplicationsOrdered(history.TagApplications));

    /// <summary>
    /// Validates that placement outcome and team presence satisfy the assignment contract.
    /// </summary>
    /// <param name="history">The campaign-history row to validate.</param>
    /// <returns><see langword="true"/> when the outcome and team relationship is valid.</returns>
    private static bool IsValidPlacementRelationship(PlayerCampaignHistoryDto history)
        => history.PlacementOutcome switch
        {
            Nova.Shared.Enums.PlacementOutcome.Assigned =>
                   history.Team is not null
                   && history.Team.TeamId > 0
                   && !string.IsNullOrWhiteSpace(history.Team.Name),
            Nova.Shared.Enums.PlacementOutcome.Undecided
                   or Nova.Shared.Enums.PlacementOutcome.NotSelected
                   or Nova.Shared.Enums.PlacementOutcome.Withdrawn => history.Team is null,
            _ => false
        };

    /// <summary>
    /// Validates the portable invariants of an evaluation-note row.
    /// </summary>
    /// <param name="note">The evaluation note to validate.</param>
    /// <returns><see langword="true"/> when the note is structurally valid.</returns>
    private static bool IsValidNote(PlayerEvaluationNoteDto note)
        => note is not null
            && note.NoteId > 0
            && !string.IsNullOrWhiteSpace(note.Content)
            && note.AuthorUserId > 0
            && !string.IsNullOrWhiteSpace(note.AuthorDisplayName)
            && note.CreatedAt != default;

    /// <summary>
    /// Validates the portable invariants of a tag-application row.
    /// </summary>
    /// <param name="application">The tag application to validate.</param>
    /// <returns><see langword="true"/> when the application is structurally valid.</returns>
    private static bool IsValidTagApplication(PlayerTagApplicationDto application)
        => application is not null
            && application.CampaignTagApplicationId > 0
            && application.PlayerTagId > 0
            && !string.IsNullOrWhiteSpace(application.TagName)
            && !string.IsNullOrWhiteSpace(application.TagColor)
            && application.ApplyingUserId > 0
            && !string.IsNullOrWhiteSpace(application.ApplyingUserDisplayName)
            && application.AppliedAt != default;

    /// <summary>
    /// Validates newest-first evaluation-note ordering with an identifier tie-breaker.
    /// </summary>
    /// <param name="notes">The notes to validate.</param>
    /// <returns><see langword="true"/> when the notes retain the contracted order.</returns>
    private static bool AreNotesOrdered(IReadOnlyList<PlayerEvaluationNoteDto> notes)
        => notes.Zip(notes.Skip(1)).All(pair =>
            pair.First.CreatedAt > pair.Second.CreatedAt
            || (pair.First.CreatedAt == pair.Second.CreatedAt
                && pair.First.NoteId > pair.Second.NoteId));

    /// <summary>
    /// Validates newest-first tag-application ordering with an identifier tie-breaker.
    /// </summary>
    /// <param name="applications">The tag applications to validate.</param>
    /// <returns><see langword="true"/> when the applications retain the contracted order.</returns>
    private static bool AreTagApplicationsOrdered(IReadOnlyList<PlayerTagApplicationDto> applications)
        => applications.Zip(applications.Skip(1)).All(pair =>
            pair.First.AppliedAt > pair.Second.AppliedAt
            || (pair.First.AppliedAt == pair.Second.AppliedAt
                && pair.First.CampaignTagApplicationId > pair.Second.CampaignTagApplicationId));
}
