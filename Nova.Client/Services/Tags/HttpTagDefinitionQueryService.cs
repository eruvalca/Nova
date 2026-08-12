using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Client.Services.Tags;

/// <summary>
/// WebAssembly client implementation of <see cref="ITagDefinitionQueryService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpTagDefinitionQueryService(HttpClient http) : ITagDefinitionQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<TagDefinitionDto>>> GetManagementListAsync(
        GetTagDefinitionsInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        using var response = await http.GetAsync(
            TagEndpoints.GetListUrl(input.Search, input.LifecycleStatus),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var result = await response.Content.ReadRequiredJsonAsync<List<TagDefinitionDto>>(
            "The server returned an invalid tag-definition list response.",
            tags => tags.All(IsValidTagDefinition),
            cancellationToken);
        return result.Match<ServiceResult<IReadOnlyList<TagDefinitionDto>>>(
            tags => tags.AsReadOnly(),
            problem => problem);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<TagDefinitionDto>>> GetChoicesAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(TagEndpoints.GetChoicesUrl(), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var result = await response.Content.ReadRequiredJsonAsync<List<TagDefinitionDto>>(
            "The server returned an invalid tag-definition choices response.",
            tags => tags.All(IsValidChoice),
            cancellationToken);
        return result.Match<ServiceResult<IReadOnlyList<TagDefinitionDto>>>(
            tags => tags.AsReadOnly(),
            problem => problem);
    }

    /// <summary>
    /// Validates the portable invariants of a tag-definition row.
    /// </summary>
    /// <param name="dto">The tag-definition row to validate.</param>
    /// <returns><see langword="true"/> when the row is structurally valid.</returns>
    private static bool IsValidTagDefinition(TagDefinitionDto dto)
        => dto is not null
            && dto.PlayerTagId > 0
            && !string.IsNullOrWhiteSpace(dto.Name)
            && !string.IsNullOrWhiteSpace(dto.Color);

    /// <summary>
    /// Validates that a choices row is structurally valid and carries an active lifecycle, since the
    /// choices endpoint only ever returns active tag definitions.
    /// </summary>
    /// <param name="dto">The tag-definition row to validate.</param>
    /// <returns><see langword="true"/> when the row is an active, structurally valid choice.</returns>
    private static bool IsValidChoice(TagDefinitionDto dto)
        => IsValidTagDefinition(dto) && dto.LifecycleStatus == LifecycleStatus.Active;
}
