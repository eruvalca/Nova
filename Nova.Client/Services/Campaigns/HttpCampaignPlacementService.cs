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
            IsValidSuccessPayload,
            cancellationToken);
    }

    /// <summary>
    /// Validates the token for the next save. An identical save preserves the submitted token.
    /// </summary>
    /// <param name="result">The success payload to validate.</param>
    /// <returns>Whether the payload contains a usable concurrency token.</returns>
    private static bool IsValidSuccessPayload(PlacementMutationSuccess result)
        => result.ConcurrencyToken != Guid.Empty;
}
