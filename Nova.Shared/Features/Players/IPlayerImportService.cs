using Nova.Shared.Results;

namespace Nova.Shared.Features.Players;

/// <summary>
/// Previews bounded CSV files and commits explicitly confirmed eligible rows with durable recovery.
/// </summary>
public interface IPlayerImportService
{
    /// <summary>Commits reviewed eligible rows or recovers the original exact-request receipt.</summary>
    /// <param name="input">The original upload and server-issued confirmation.</param>
    /// <param name="cancellationToken">Cancels processing; cancellation does not prove rollback.</param>
    /// <returns>The immutable completion or a safe failure.</returns>
    Task<ServiceResult<PlayerImportCompletion>> CommitAsync(
        PlayerImportCommitInput input,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the authoritative CSV template for the current club administrator.</summary>
    Task<ServiceResult<PlayerImportTemplate>> GetTemplateAsync(CancellationToken cancellationToken = default);

    /// <summary>Parses and validates an uploaded CSV without persisting any rows.</summary>
    Task<ServiceResult<PlayerImportPreview>> PreviewAsync(
        PlayerImportUploadInput upload,
        CancellationToken cancellationToken = default);
}
