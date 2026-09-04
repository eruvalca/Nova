using System.Net.Http.Json;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;
using OneOf.Types;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignLifecycleService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpCampaignLifecycleService(HttpClient http) : ICampaignLifecycleService
{
    /// <inheritdoc />
    public async Task<ServiceResult<OpenCampaignResult>> OpenAsync(
        long campaignId,
        OpenCampaignInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        using var response = await http.PostAsJsonAsync(
            CampaignEndpoints.OpenUrl(campaignId),
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<OpenCampaignResult>(
            "The server returned an invalid campaign opening response.",
            result => IsValidOpenResult(result, campaignId, input.OperationId),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> DeleteDraftAsync(
        long campaignId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync(CampaignEndpoints.DeleteDraftUrl(campaignId), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }

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

    /// <summary>
    /// Validates an immutable opening receipt returned by the server.
    /// </summary>
    /// <param name="result">The deserialized opening receipt.</param>
    /// <param name="campaignId">The requested campaign identifier.</param>
    /// <param name="operationId">The requested operation identifier.</param>
    /// <returns><see langword="true"/> when the receipt is internally consistent.</returns>
    private static bool IsValidOpenResult(OpenCampaignResult result, long campaignId, Guid operationId)
        => result.OperationId == operationId
            && result.CampaignId == campaignId
            && result.OpenedAt != default
            && result.OpenedByUserId > 0
            && result.EnrolledPlayerCount > 0
            && result.ActiveTeamCount >= 0
            && result.Warnings is not null
            && result.Warnings.All(Enum.IsDefined)
            && result.Warnings.Distinct().Count() == result.Warnings.Count
            && result.Warnings.Contains(CampaignOpeningWarning.NoActiveTeams) == (result.ActiveTeamCount == 0);
}
