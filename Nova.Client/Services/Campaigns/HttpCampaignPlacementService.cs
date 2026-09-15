using System.Net.Http.Json;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignPlacementService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
internal sealed class HttpCampaignPlacementService(HttpClient http) : ICampaignPlacementService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlacementMutationSuccess>> UpdatePlacementAsync(
        UpdateCampaignPlacementInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0) { return ServiceProblem.Validation(errors); }
        using var response = await http.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(input.PlayerCampaignAssignmentId),
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<PlacementMutationSuccess>(
            "The server returned an invalid campaign placement update response.",
            result => IsValidSuccessPayload(result, input),
            cancellationToken);
    }

    /// <summary>
    /// Validates the token for the next save. An identical save preserves the submitted token.
    /// </summary>
    /// <param name="result">The success payload to validate.</param>
    /// <returns>Whether the payload contains a usable concurrency token.</returns>
    private static bool IsValidSuccessPayload(PlacementMutationSuccess result, UpdateCampaignPlacementInput input)
        => result.ConcurrencyToken != Guid.Empty && result.Receipt is { Decision: { } decision } receipt
            && receipt.OperationId == input.OperationId && receipt.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId
            && decision.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId
            && decision.ConcurrencyToken == result.ConcurrencyToken && decision.Outcome == input.Outcome
            && decision.TeamId == input.TeamId && decision.PlayerId > 0 && decision.CampaignId > 0
            && decision.SeasonId > 0 && decision.SeasonOpeningSequence > 0 && decision.RecordedById > 0
            && !string.IsNullOrWhiteSpace(decision.ActorDisplayName) && decision.RecordedAt > DateTimeOffset.UnixEpoch
            && receipt.CommittedAt >= decision.RecordedAt && receipt.RecoveryExpiresAt > receipt.CommittedAt
            && receipt.RecoveryExpiresAt - receipt.CommittedAt <= TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1);
}
