using System.Net.Http.Json;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;

namespace Nova.Client.Services.Campaigns;

/// <summary>Strict HTTP transport for versioned, replayable evaluation notes.</summary>
/// <param name="http">The authenticated application HTTP client.</param>
internal sealed class HttpCampaignEvaluationNoteService(HttpClient http) : ICampaignEvaluationNoteService
{
    /// <inheritdoc />
    public Task<ServiceResult<EvaluationNoteMutationSuccess>> AddAsync(AddEvaluationNoteInput input, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, CampaignEndpoints.AddEvaluationNote, input, input.OperationId,
            result => result.Receipt.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<EvaluationNoteMutationSuccess>> EditAsync(EditEvaluationNoteInput input, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Put, CampaignEndpoints.EditEvaluationNoteUrl(input.NoteId),
            new PutEvaluationNoteInput { Content = input.Content, ExpectedVersion = input.ExpectedVersion, OperationId = input.OperationId },
            input.OperationId, result => result.NoteId == input.NoteId && result.Version != input.ExpectedVersion, cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<EvaluationNoteMutationSuccess>> DeleteAsync(DeleteEvaluationNoteInput input, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, CampaignEndpoints.DeleteEvaluationNoteUrl(input.NoteId), input,
            input.OperationId, result => result.NoteId == input.NoteId && result.Version == input.ExpectedVersion, cancellationToken);

    /// <summary>Retains caller identity and rejects incomplete, mismatched or malformed success payloads.</summary>
    private async Task<ServiceResult<EvaluationNoteMutationSuccess>> SendAsync<T>(HttpMethod method, string route, T body,
        Guid operationId, Func<EvaluationNoteMutationSuccess, bool> matchesSubject, CancellationToken token)
    {
        using var request = new HttpRequestMessage(method, route) { Content = JsonContent.Create(body) };
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(token);
        }

        return await response.Content.ReadRequiredJsonAsync<EvaluationNoteMutationSuccess>(
            "The server did not return a valid note receipt. Retry the original operation to recover its result.",
            result => result.NoteId > 0 && result.Version != Guid.Empty
                && EvaluationReceiptValidation.IsValid(result.Receipt, operationId) && matchesSubject(result), token);
    }
}
