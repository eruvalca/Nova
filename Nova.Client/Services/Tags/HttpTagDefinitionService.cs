using System.Net.Http.Json;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;

namespace Nova.Client.Services.Tags;

/// <summary>
/// WebAssembly client implementation of <see cref="ITagDefinitionService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpTagDefinitionService(HttpClient http) : ITagDefinitionService
{
    public async Task<ServiceResult<TagDefinitionMutationSuccess>> CreateAsync(
        CreateTagDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(TagDefinitionEndpoints.Create, input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<TagDefinitionMutationSuccess>(
            "The server returned an invalid tag-definition payload.",
            IsValidTagDefinitionSuccess,
            cancellationToken);
    }

    public async Task<ServiceResult<TagDefinitionMutationSuccess>> UpdateAsync(
        UpdateTagDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(TagDefinitionEndpoints.UpdateUrl(input.TagDefinitionId), input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<TagDefinitionMutationSuccess>(
            "The server returned an invalid tag-definition payload.",
            IsValidTagDefinitionSuccess,
            cancellationToken);
    }

    public async Task<ServiceResult<IReadOnlyList<TagDefinitionSummary>>> GetActiveAsync(
        GetTagDefinitionsInput? input = null,
        CancellationToken cancellationToken = default)
    {
        var url = input is null ? $"{TagDefinitionEndpoints.GroupPrefix}/active" : $"{TagDefinitionEndpoints.GroupPrefix}/active?limit={Math.Clamp(input.Limit ?? 50, 1, 100)}";
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<IReadOnlyList<TagDefinitionSummary>>(
            "The server returned an invalid active-tag-definition list.",
            list => list is not null,
            cancellationToken);
    }

    public async Task<ServiceResult<IReadOnlyList<TagDefinitionSummary>>> GetArchivedAsync(
        GetTagDefinitionsInput? input = null,
        CancellationToken cancellationToken = default)
    {
        var url = input is null ? $"{TagDefinitionEndpoints.GroupPrefix}/archived" : $"{TagDefinitionEndpoints.GroupPrefix}/archived?limit={Math.Clamp(input.Limit ?? 50, 1, 100)}";
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<IReadOnlyList<TagDefinitionSummary>>(
            "The server returned an invalid archived-tag-definition list.",
            list => list is not null,
            cancellationToken);
    }

    public async Task<ServiceResult<TagDefinitionMutationSuccess>> ArchiveAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(TagDefinitionEndpoints.ArchiveUrl(tagDefinitionId), null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<TagDefinitionMutationSuccess>(
            "The server returned an invalid tag-definition payload.",
            IsValidTagDefinitionSuccess,
            cancellationToken);
    }

    public async Task<ServiceResult<TagDefinitionMutationSuccess>> RestoreAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(TagDefinitionEndpoints.RestoreUrl(tagDefinitionId), null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<TagDefinitionMutationSuccess>(
            "The server returned an invalid tag-definition payload.",
            IsValidTagDefinitionSuccess,
            cancellationToken);
    }

    private static bool IsValidTagDefinitionSuccess(TagDefinitionMutationSuccess value)
        => value is not null && value.TagDefinitionId > 0;
}
