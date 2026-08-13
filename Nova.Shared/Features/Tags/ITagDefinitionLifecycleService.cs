using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Shared.Features.Tags;

/// <summary>
/// Provides administrator-only tag-definition lifecycle mutations.
/// </summary>
public interface ITagDefinitionLifecycleService
{
    /// <summary>
    /// Archives a tag definition while preserving existing player associations.
    /// </summary>
    /// <param name="tagDefinitionId">The tag-definition identifier to archive.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A success result or a structured service problem.</returns>
    Task<ServiceResult<Success>> ArchiveAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores an archived tag definition to active use.
    /// </summary>
    /// <param name="tagDefinitionId">The tag-definition identifier to restore.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A success result or a service problem.</returns>
    Task<ServiceResult<Success>> RestoreAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default);
}
