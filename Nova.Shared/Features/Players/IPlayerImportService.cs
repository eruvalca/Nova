using Nova.Shared.Results;

namespace Nova.Shared.Features.Players;

/// <summary>
/// Generates and previews bounded player CSV imports without committing player records.
/// </summary>
public interface IPlayerImportService
{
    /// <summary>Gets the authoritative CSV template for the current club administrator.</summary>
    Task<ServiceResult<PlayerImportTemplate>> GetTemplateAsync(CancellationToken cancellationToken = default);

    /// <summary>Parses and validates an uploaded CSV without persisting any rows.</summary>
    Task<ServiceResult<PlayerImportPreview>> PreviewAsync(
        PlayerImportUpload upload,
        CancellationToken cancellationToken = default);
}
