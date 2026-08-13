using Nova.Shared.Results;

namespace Nova.Shared.Features.Tags;

/// <summary>
/// Provides administrator tag-definition creation and permanent-profile editing operations.
/// </summary>
public interface ITagDefinitionService
{
    /// <summary>
    /// Creates an active tag definition for the current club.
    /// </summary>
    /// <param name="input">The new tag definition's profile.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The created tag definition or a service problem.</returns>
    Task<ServiceResult<TagDefinitionDto>> CreateAsync(
        CreateTagDefinitionInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an active tag definition's name and color.
    /// </summary>
    /// <param name="input">The requested tag-definition profile.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The updated tag definition or a service problem.</returns>
    Task<ServiceResult<TagDefinitionDto>> UpdateAsync(
        UpdateTagDefinitionInput input,
        CancellationToken cancellationToken = default);
}
