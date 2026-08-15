using System.Net.Http.Json;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignPlacementService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpCampaignPlacementService(HttpClient http) : ICampaignPlacementService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlacementMutationSuccess>> UpdatePlacementAsync(
        UpdateCampaignPlacementInput input,
        CancellationToken cancellationToken = default)
    {
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
            result => IsValidSuccessPayload(result, input.ExpectedConcurrencyToken),
            cancellationToken);
    }

    /// <summary>
    /// Validates that a placement success payload carries a fresh concurrency token.
    /// </summary>
    /// <param name="result">The success payload to validate.</param>
    /// <param name="expectedConcurrencyToken">The token submitted with the request, which the response token must replace.</param>
    /// <returns><see langword="true"/> when the token is a valid replacement for the submitted token.</returns>
    private static bool IsValidSuccessPayload(
        PlacementMutationSuccess result,
        Guid expectedConcurrencyToken)
        => result.ConcurrencyToken != Guid.Empty
            && result.ConcurrencyToken != expectedConcurrencyToken;
}
