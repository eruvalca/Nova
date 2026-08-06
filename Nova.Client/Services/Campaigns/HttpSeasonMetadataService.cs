using System.Net.Http.Json;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ISeasonMetadataService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpSeasonMetadataService(HttpClient http) : ISeasonMetadataService
{
    /// <inheritdoc />
    public async Task<ServiceResult<UpdateSeasonMetadataResult>> UpdateAsync(
        UpdateSeasonMetadataInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(
            CampaignEndpoints.UpdateSeasonMetadata,
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<UpdateSeasonMetadataResult>(
            "The server returned an invalid season metadata update response.",
            result => IsValidSuccessPayload(result, input.SeasonId),
            cancellationToken);
    }

    private static bool IsValidSuccessPayload(
        UpdateSeasonMetadataResult result,
        long expectedSeasonId)
        => result.SeasonId == expectedSeasonId
            && result.SeasonId > 0
            && !string.IsNullOrWhiteSpace(result.Name)
            && result.StartDate != default
            && (result.EndDate is null || result.EndDate >= result.StartDate);
}
