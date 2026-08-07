using System.Net.Http.Json;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignMetadataService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpCampaignMetadataService(HttpClient http) : ICampaignMetadataService
{
    /// <inheritdoc />
    public async Task<ServiceResult<UpdateCampaignMetadataResult>> UpdateAsync(
        UpdateCampaignMetadataInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignMetadata,
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<UpdateCampaignMetadataResult>(
            "The server returned an invalid campaign metadata update response.",
            result => IsValidSuccessPayload(result, input.CampaignId),
            cancellationToken);
    }

    private static bool IsValidSuccessPayload(
        UpdateCampaignMetadataResult result,
        long expectedCampaignId)
        => result.CampaignId == expectedCampaignId
            && result.CampaignId > 0
            && !string.IsNullOrWhiteSpace(result.Name)
            && result.StartDate != default
            && (result.PlannedEndDate is null || result.PlannedEndDate >= result.StartDate)
            && result.SeasonId > 0
            && !string.IsNullOrWhiteSpace(result.SeasonName);
}
