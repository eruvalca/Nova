using System.Net.Http.Json;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly client implementation of <see cref="ICampaignEvaluationNoteService"/> that calls campaign evaluation note endpoints.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpCampaignEvaluationNoteService(HttpClient http) : ICampaignEvaluationNoteService
{
    /// <inheritdoc />
    public async Task<ServiceResult<EvaluationNoteMutationSuccess>> AddAsync(
        AddEvaluationNoteInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<EvaluationNoteMutationSuccess>(
            "The server returned an invalid evaluation note response.",
            result => result.NoteId > 0,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> EditAsync(
        EditEvaluationNoteInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(
            CampaignEndpoints.EditEvaluationNoteUrl(input.NoteId),
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> DeleteAsync(
        long noteId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync(
            CampaignEndpoints.DeleteEvaluationNoteUrl(noteId),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }
}
