using System.Net.Http.Json;
using Nova.Shared.Enums;
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
        var limit = Math.Clamp(input?.Limit ?? 50, 1, 100);
        var url = $"{TagDefinitionEndpoints.GroupPrefix}/active?limit={limit}";
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<IReadOnlyList<TagDefinitionSummary>>(
            "The server returned an invalid active-tag-definition list.",
            list => IsValidActiveTagDefinitionList(list),
            cancellationToken);
    }

    public async Task<ServiceResult<IReadOnlyList<TagDefinitionSummary>>> GetArchivedAsync(
        GetTagDefinitionsInput? input = null,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(input?.Limit ?? 50, 1, 100);
        var url = $"{TagDefinitionEndpoints.GroupPrefix}/archived?limit={limit}";
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<IReadOnlyList<TagDefinitionSummary>>(
            "The server returned an invalid archived-tag-definition list.",
            list => IsValidArchivedTagDefinitionList(list),
            cancellationToken);
    }

    private static bool IsValidTagDefinitionList(IReadOnlyList<TagDefinitionSummary>? list)
        => list is not null
            && list.Count <= 100
            && list.All(tag => tag.TagDefinitionId > 0
                && !string.IsNullOrWhiteSpace(tag.Name)
                && tag.Name.Length <= 80
                && tag.Color is { Length: 7 }
                && tag.Color.StartsWith('#')
                && tag.Color[1..].All(char.IsAsciiHexDigit)
                && tag.CreatedAt != default
                && tag.ArchivedAt is not null == (tag.LifecycleStatus == LifecycleStatus.Archived));

    private static bool IsValidActiveTagDefinitionList(IReadOnlyList<TagDefinitionSummary>? list)
        => IsValidTagDefinitionList(list)
            && list!.All(tag => tag.LifecycleStatus == LifecycleStatus.Active && tag.ArchivedAt is null);

    private static bool IsValidArchivedTagDefinitionList(IReadOnlyList<TagDefinitionSummary>? list)
        => IsValidTagDefinitionList(list)
            && list!.All(tag => tag.LifecycleStatus == LifecycleStatus.Archived && tag.ArchivedAt.HasValue);

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
