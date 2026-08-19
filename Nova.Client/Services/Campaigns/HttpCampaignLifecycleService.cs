using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignLifecycleService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpCampaignLifecycleService(HttpClient http) : ICampaignLifecycleService
{
    /// <inheritdoc />
    public Task<ServiceResult<Success>> CloseAsync(
        long campaignId,
        CancellationToken cancellationToken = default)
        => SendMutationAsync(CampaignEndpoints.CloseUrl(campaignId), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<Success>> ReopenAsync(
        long campaignId,
        CancellationToken cancellationToken = default)
        => SendMutationAsync(CampaignEndpoints.ReopenUrl(campaignId), cancellationToken);

    /// <summary>
    /// Posts a bodyless lifecycle mutation and converts the response to a service result.
    /// </summary>
    /// <param name="requestUri">The campaign lifecycle endpoint URL.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A success result on 204 or a structured service problem on failure.</returns>
    private async Task<ServiceResult<Success>> SendMutationAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(requestUri, content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }
}
