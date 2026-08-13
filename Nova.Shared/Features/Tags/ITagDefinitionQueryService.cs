using Nova.Shared.Results;

namespace Nova.Shared.Features.Tags;

/// <summary>
/// Provides tenant-safe read access to the current club's tag definitions.
/// </summary>
public interface ITagDefinitionQueryService
{
    /// <summary>
    /// Retrieves tag definitions matching the requested management filters for club administrators.
    /// </summary>
    /// <param name="input">The optional management filters.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The bounded matching tag definitions (with truncation metadata) or a service problem.</returns>
    Task<ServiceResult<TagDefinitionListResult>> GetManagementListAsync(
        GetTagDefinitionsInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves active tag-definition choices for authenticated club members.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The active tag definitions or a service problem.</returns>
    Task<ServiceResult<IReadOnlyList<TagDefinitionDto>>> GetChoicesAsync(
        CancellationToken cancellationToken = default);
}
