using System.Net.Http.Json;
using System.Text.Json;
using Nova.Shared.Campaigns;
using Nova.Shared.Results;

namespace Nova.Client.Services;

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

        try
        {
            var result = await response.Content.ReadFromJsonAsync<CreateCampaignResult>(
                cancellationToken);
            return result is null
                ? ServiceProblem.ServerError(
                    "The server returned an empty campaign creation response.")
                : result;
        }
        catch (JsonException)
        {
            return ServiceProblem.ServerError(
                "The server returned an invalid campaign creation response.");
        }
    }
}
