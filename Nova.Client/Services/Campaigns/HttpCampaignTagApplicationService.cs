using System.Net.Http.Json;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;

namespace Nova.Client.Services.Campaigns;

/// <summary>Strict HTTP transport for atomic trait capture and exact-operation recovery.</summary>
/// <param name="http">The authenticated application HTTP client.</param>
internal sealed class HttpCampaignTagApplicationService(HttpClient http) : ICampaignTagApplicationService
{
    /// <inheritdoc />
    public Task<ServiceResult<CampaignTagApplicationMutationSuccess>> ApplyAsync(ApplyCampaignTagApplicationInput input, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, CampaignEndpoints.ApplyCampaignTagApplication, input, input.OperationId,
            result => result.Receipt.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId && result.PlayerTagId == input.PlayerTagId, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<CampaignTagApplicationMutationSuccess>> CreateAndApplyAsync(CreateAndApplyCampaignTagInput input, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, CampaignEndpoints.CreateAndApplyCampaignTag, input, input.OperationId,
            result => result.Receipt.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<CampaignTagApplicationMutationSuccess>> RemoveAsync(RemoveCampaignTagApplicationInput input, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, CampaignEndpoints.RemoveCampaignTagApplicationUrl(input.CampaignTagApplicationId), input, input.OperationId,
            result => result.CampaignTagApplicationId == input.CampaignTagApplicationId && !result.AlreadyApplied, cancellationToken);

    /// <summary>Checks both receipt identity and subject before accepting a successful response.</summary>
    private async Task<ServiceResult<CampaignTagApplicationMutationSuccess>> SendAsync<T>(HttpMethod method, string route, T body,
        Guid operationId, Func<CampaignTagApplicationMutationSuccess, bool> matchesSubject, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, route) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(token);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignTagApplicationMutationSuccess>(
            "The server did not return a valid trait receipt. Retry the original operation to recover its result.",
            result => result.CampaignTagApplicationId > 0 && result.PlayerTagId > 0
                && EvaluationReceiptValidation.IsValid(result.Receipt, operationId) && matchesSubject(result), token);
    }
}
