using System.Net.Http.Json;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;

namespace Nova.Client.Services.Tags;

/// <summary>
/// WebAssembly client implementation of <see cref="ITagDefinitionService"/> that calls the
/// server's tag-definition management endpoints over HTTP.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpTagDefinitionService(HttpClient http) : ITagDefinitionService
{
    /// <inheritdoc />
    public async Task<ServiceResult<TagDefinitionDto>> CreateAsync(
        CreateTagDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(TagEndpoints.Create, input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<TagDefinitionDto>(
            "The server returned an invalid tag-definition response.",
            dto => IsValidTagDefinition(dto),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TagDefinitionDto>> UpdateAsync(
        UpdateTagDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(
            TagEndpoints.UpdateUrl(input.TagId),
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<TagDefinitionDto>(
            "The server returned an invalid tag-definition response.",
            dto => IsValidTagDefinition(dto, input.TagId),
            cancellationToken);
    }

    /// <summary>
    /// Validates the portable invariants of a tag-definition success payload.
    /// </summary>
    /// <param name="dto">The tag definition to validate.</param>
    /// <param name="expectedTagId">The expected tag-definition identifier, when known.</param>
    /// <returns><see langword="true"/> when the tag definition is structurally valid.</returns>
    private static bool IsValidTagDefinition(TagDefinitionDto dto, long? expectedTagId = null)
        => dto is not null
            && dto.PlayerTagId > 0
            && (expectedTagId is null || dto.PlayerTagId == expectedTagId)
            && !string.IsNullOrWhiteSpace(dto.Name)
            && !string.IsNullOrWhiteSpace(dto.Color)
            && dto.LifecycleStatus is LifecycleStatus.Active or LifecycleStatus.Archived;
}
