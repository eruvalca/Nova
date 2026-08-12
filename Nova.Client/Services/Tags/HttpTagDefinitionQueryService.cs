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
            tags => tags.Count <= TagDefinitionLimits.MaxTagDefinitions
                && tags.All(tag => IsValidForView(tag, input.LifecycleStatus)),
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
            tags => tags.Count <= TagDefinitionLimits.MaxTagDefinitions && tags.All(IsValidChoice),
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
            && dto.Name.Length <= 100
            && IsValidColor(dto.Color);

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
            if (!Uri.IsHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates that a management-list row is structurally valid, carries a defined lifecycle, and
    /// matches the requested view so a stale or malformed response cannot surface mismatched rows.
    /// </summary>
    /// <param name="dto">The tag-definition row to validate.</param>
    /// <param name="lifecycleStatus">The requested lifecycle view.</param>
    /// <returns><see langword="true"/> when the row is structurally valid and matches the requested view.</returns>
    private static bool IsValidForView(TagDefinitionDto dto, string? lifecycleStatus)
    {
        if (!IsValidTagDefinition(dto) || !Enum.IsDefined(dto.LifecycleStatus))
        {
            return false;
        }

        return lifecycleStatus?.Trim().ToLowerInvariant() switch
        {
            "active" => dto.LifecycleStatus == LifecycleStatus.Active,
            "archived" => dto.LifecycleStatus == LifecycleStatus.Archived,
            _ => true
        };
    }

    /// <summary>
    /// Validates that a choices row is structurally valid and carries an active lifecycle, since the
    /// choices endpoint only ever returns active tag definitions.
    /// </summary>
    /// <param name="dto">The tag-definition row to validate.</param>
    /// <returns><see langword="true"/> when the row is an active, structurally valid choice.</returns>
    private static bool IsValidChoice(TagDefinitionDto dto)
        => IsValidTagDefinition(dto) && dto.LifecycleStatus == LifecycleStatus.Active;
}
