using System.Net.Http.Json;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Client.Services.Campaigns;

/// <summary>Reads independent evidence regions with strict keyset and payload validation.</summary>
/// <param name="http">The authenticated application client.</param>
internal sealed class HttpCampaignEvaluationQueryService(HttpClient http) : ICampaignEvaluationQueryService
{
    /// <inheritdoc />
    public Task<ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>> GetNotesAsync(GetEvaluationHistoryInput input, CancellationToken cancellationToken = default) =>
        ReadHistoryAsync(input, CampaignEndpoints.EvaluationNotesUrl, (EvaluationHistoryPage<CampaignParticipantNoteDto> page) =>
            ValidPage(page, input, note => (note.CreatedAt, note.NoteId), note => note is not null && note.NoteId > 0
                && note.Version != Guid.Empty && !string.IsNullOrWhiteSpace(note.Content) && note.Content.Length <= 4000
                && !string.IsNullOrWhiteSpace(note.AuthorDisplayName) && (note.ModifiedAt is null || note.ModifiedAt >= note.CreatedAt)), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>> GetApplicationsAsync(GetEvaluationHistoryInput input, CancellationToken cancellationToken = default) =>
        ReadHistoryAsync(input, CampaignEndpoints.EvaluationApplicationsUrl, (EvaluationHistoryPage<CampaignParticipantTagApplicationDto> page) =>
            ValidPage(page, input, application => (application.AppliedAt, application.CampaignTagApplicationId), application => application is not null
                && application.CampaignTagApplicationId > 0 && application.PlayerTagId > 0 && !string.IsNullOrWhiteSpace(application.TagName)
                && !string.IsNullOrWhiteSpace(application.TagColor) && !string.IsNullOrWhiteSpace(application.ActorDisplayName)
                && (!application.IsArchived || !application.CanRemove)), cancellationToken);

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<EvaluationTagChoice>>> GetTagChoicesAsync(GetCampaignParticipantDetailInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        var result = await ReadAsync(CampaignEndpoints.EvaluationTagChoicesUrl(input), (List<EvaluationTagChoice> choices) =>
            choices.Count <= TagDefinitionLimits.MaxActiveTagDefinitions && choices.All(choice => choice is not null && choice.PlayerTagId > 0
                && !string.IsNullOrWhiteSpace(choice.Name) && !string.IsNullOrWhiteSpace(choice.Color)
                && (choice.ApplicationId is null || choice.ApplicationId > 0))
            && choices.Select(choice => choice.PlayerTagId).Distinct().Count() == choices.Count, cancellationToken);
        return result.Match<ServiceResult<IReadOnlyList<EvaluationTagChoice>>>(choices => choices.AsReadOnly(), problem => problem);
    }

    private Task<ServiceResult<T>> ReadHistoryAsync<T>(GetEvaluationHistoryInput input,
        Func<GetEvaluationHistoryInput, string> route, Func<T, bool> validate, CancellationToken token)
    {
        var errors = InputValidator.Validate(input);
        return errors.Count > 0
            ? Task.FromResult(new ServiceResult<T>(ServiceProblem.Validation(errors)))
            : ReadAsync(route(input), validate, token);
    }
    /// <summary>Reads one required JSON region without manufacturing an empty success.</summary>
    private async Task<ServiceResult<T>> ReadAsync<T>(string route, Func<T, bool> validate, CancellationToken token)
    {
        using var response = await http.GetAsync(new Uri(route, UriKind.RelativeOrAbsolute), token);
        return !response.IsSuccessStatusCode ? await response.ToServiceProblemAsync(token)
            : await response.Content.ReadRequiredJsonAsync("The server returned invalid evaluation evidence. Retry this section.", validate, token);
    }

    /// <summary>Rejects oversized, unordered, duplicate, out-of-cursor, or falsely continued pages.</summary>
    private static bool ValidPage<T>(EvaluationHistoryPage<T> page, GetEvaluationHistoryInput input,
        Func<T, (DateTimeOffset At, long Id)> identity, Func<T, bool> validItem)
    {
        if (page.Items is null || page.Items.Count > 20 || !page.Items.All(validItem))
        {
            return false;
        }

        var previous = input.BeforeCreatedAt is { } before && input.BeforeId is { } beforeId ? (before, beforeId) : ((DateTimeOffset, long)?)null;
        foreach (var item in page.Items)
        {
            var current = identity(item);
            if (current.At <= DateTimeOffset.UnixEpoch || (previous is { } prior
                && (current.At > prior.Item1 || (current.At == prior.Item1 && current.Id >= prior.Item2))))
            {
                return false;
            }

            previous = current;
        }

        return page.Next is null || (page.Items.Count == 20 && previous is { } last
            && page.Next.CreatedAt == last.Item1 && page.Next.Id == last.Item2);
    }
}
