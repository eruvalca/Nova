using Nova.Shared.Results;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Corrects season metadata across server and HTTP boundaries.
/// </summary>
public interface ISeasonMetadataService
{
    /// <summary>
    /// Updates a season's name, start date, and optional end date.
    /// Linked campaigns and enrollment are preserved.
    /// </summary>
    /// <param name="input">The season metadata correction request.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The updated season metadata or a ProblemDetails-mappable failure.</returns>
    Task<ServiceResult<UpdateSeasonMetadataResult>> UpdateAsync(
        UpdateSeasonMetadataInput input,
        CancellationToken cancellationToken = default);
}
