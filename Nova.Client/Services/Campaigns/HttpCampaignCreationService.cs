using System.Net.Http.Json;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignCreationService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpCampaignCreationService(HttpClient http) : ICampaignCreationService
{
    /// <inheritdoc />
    public async Task<ServiceResult<CreateCampaignResult>> CreateAsync(
        CreateCampaignInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            CampaignEndpoints.Create,
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CreateCampaignResult>(
            "The server returned an invalid campaign creation response.",
            result => IsValidSuccessPayload(result, input.OperationId),
            cancellationToken);
    }

    private static bool IsValidSuccessPayload(
        CreateCampaignResult result,
        Guid expectedOperationId)
        => result.OperationId == expectedOperationId
            && result.CampaignId > 0
            && !string.IsNullOrWhiteSpace(result.CampaignName)
            && result.CampaignStartDate != default
            && (result.CampaignPlannedEndDate is null
                || result.CampaignPlannedEndDate >= result.CampaignStartDate)
            && result.Status == CampaignStatus.Active
            && result.SeasonId > 0
            && !string.IsNullOrWhiteSpace(result.SeasonName)
            && result.SeasonStartDate != default
            && (result.SeasonEndDate is null
                || result.SeasonEndDate >= result.SeasonStartDate)
            && result.CampaignStartDate >= result.SeasonStartDate
            && (result.SeasonEndDate is null
                || result.CampaignPlannedEndDate <= result.SeasonEndDate)
            && result.EnrolledPlayerCount >= 0;
}
