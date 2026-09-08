using System.Net.Http.Json;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using OneOf.Types;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly client implementation of <see cref="ICampaignTagApplicationService"/> that calls campaign tag application endpoints.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
internal sealed class HttpCampaignTagApplicationService(HttpClient http) : ICampaignTagApplicationService
{
    /// <inheritdoc />
    public async Task<ServiceResult<CampaignTagApplicationMutationSuccess>> ApplyAsync(
        ApplyCampaignTagApplicationInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignTagApplicationMutationSuccess>(
            "The server returned an invalid campaign tag application response.",
            result => result.CampaignTagApplicationId > 0,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> RemoveAsync(
        RemoveCampaignTagApplicationInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync(
new Uri(CampaignEndpoints.RemoveCampaignTagApplicationUrl(input.CampaignTagApplicationId), UriKind.RelativeOrAbsolute),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }
}
