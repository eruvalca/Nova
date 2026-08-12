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
            dto => IsValidTagDefinition(dto, input.TagId, requiredStatus: null),
            cancellationToken);
    }

    /// <summary>
    /// Validates the portable invariants of a tag-definition success payload.
    /// </summary>
    /// <param name="dto">The tag definition to validate.</param>
    /// <param name="expectedTagId">The expected tag-definition identifier, when known.</param>
    /// <param name="requiredStatus">
    /// The lifecycle status the payload must carry, or <see langword="null"/> to accept any defined
    /// status. Update passes <see langword="null"/> because its ambiguous-commit verifier re-reads the
    /// current row, so a concurrent archive can legitimately return an <see cref="LifecycleStatus.Archived"/>
    /// snapshot after a successful update; create always requires <see cref="LifecycleStatus.Active"/>.
    /// </param>
    /// <returns><see langword="true"/> when the tag definition is structurally valid.</returns>
    private static bool IsValidTagDefinition(
        TagDefinitionDto dto,
        long? expectedTagId = null,
        LifecycleStatus? requiredStatus = LifecycleStatus.Active)
        => dto is not null
            && dto.PlayerTagId > 0
            && (expectedTagId is null || dto.PlayerTagId == expectedTagId)
            && !string.IsNullOrWhiteSpace(dto.Name)
            && dto.Name.Length <= 100
            && IsValidColor(dto.Color)
            && Enum.IsDefined(dto.LifecycleStatus)
            && (requiredStatus is null || dto.LifecycleStatus == requiredStatus);

    /// <summary>
    /// Validates a tag color as the normalized <c>#RRGGBB</c> hex form the server promises.
    /// </summary>
    /// <param name="color">The color to validate.</param>
    /// <returns><see langword="true"/> when the color matches the shared contract.</returns>
    private static bool IsValidColor(string? color)
    {
        if (color is null || color.Length != 7 || color[0] != '#')
        {
            return false;
        }

        foreach (var character in color.AsSpan(1))
        {
            if (character is not (>= '0' and <= '9' or >= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }
}
