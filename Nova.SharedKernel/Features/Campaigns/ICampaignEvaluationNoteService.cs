using Nova.SharedKernel.Results;


namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>
/// Adds, edits, and deletes evaluation notes scoped to campaign participations in the current club tenant.
/// </summary>
public interface ICampaignEvaluationNoteService
{
    /// <summary>
    /// Adds one evaluation note to a campaign participation in an Active campaign.
    /// </summary>
    /// <param name="input">The target participation and note content.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The created note identifier or a structured service problem.</returns>
    Task<ServiceResult<EvaluationNoteMutationSuccess>> AddAsync(
        AddEvaluationNoteInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Edits one evaluation note when the author supplies the current version.
    /// </summary>
    /// <param name="input">The note identifier and updated content.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A success result or a structured service problem.</returns>
    Task<ServiceResult<EvaluationNoteMutationSuccess>> EditAsync(
        EditEvaluationNoteInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one evaluation note when the author supplies the current version.
    /// </summary>
    /// <param name="input">The note identity, expected version and retained operation.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A success result or a structured service problem.</returns>
    Task<ServiceResult<EvaluationNoteMutationSuccess>> DeleteAsync(
        DeleteEvaluationNoteInput input,
        CancellationToken cancellationToken = default);
}
