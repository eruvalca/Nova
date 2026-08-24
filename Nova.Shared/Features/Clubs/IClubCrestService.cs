using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Shared.Features.Clubs;

/// <summary>
/// Represents a club crest upload payload (the raw image file as selected by the user).
/// </summary>
/// <param name="Content">The raw image bytes.</param>
/// <param name="ContentType">The content type declared by the client (must match the actual image format).</param>
public sealed record ClubCrestUpload(byte[] Content, string ContentType);

/// <summary>
/// Provides club crest operations for a specific club. Implemented directly against
/// blob storage and the database on the server, and over HTTP in the WebAssembly client.
/// </summary>
public interface IClubCrestService
{
    /// <summary>
    /// Validates, processes, and stores the supplied crest for the given club, replacing
    /// any existing crest. The caller must be a club admin of the club.
    /// </summary>
    /// <param name="clubId">The id of the club whose crest to change.</param>
    /// <param name="upload">The crest upload payload.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing <see cref="Success"/> on success,
    /// or a <see cref="ServiceProblem"/> on failure (validation, forbidden, or processing errors).
    /// </returns>
    Task<ServiceResult<Success>> ChangeClubCrestAsync(long clubId, ClubCrestUpload upload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the given club's crest and deletes its stored blobs. The caller must be a
    /// club admin of the club.
    /// </summary>
    /// <param name="clubId">The id of the club whose crest to remove.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing <see cref="Success"/> on success,
    /// or a <see cref="ServiceProblem"/> on failure (forbidden or processing errors).
    /// </returns>
    Task<ServiceResult<Success>> RemoveClubCrestAsync(long clubId, CancellationToken cancellationToken = default);
}
