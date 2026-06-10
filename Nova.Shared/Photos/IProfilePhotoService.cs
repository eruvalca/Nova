using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Shared.Photos;

/// <summary>
/// Describes the current user's saved profile photo.
/// </summary>
/// <param name="NovaUserId">The id of the user the photo belongs to.</param>
/// <param name="ContentType">The content type of the original cropped upload, when known.</param>
public sealed record ProfilePhotoInfo(long NovaUserId, string? ContentType);

/// <summary>
/// Represents a profile photo upload payload (the already-cropped image produced by the editor).
/// </summary>
/// <param name="Content">The raw image bytes.</param>
/// <param name="ContentType">The content type of the image (must be one of <see cref="ProfilePhotoConstraints.AllowedContentTypes"/>).</param>
/// <param name="FileName">The original file name, used for diagnostics only.</param>
public sealed record ProfilePhotoUpload(byte[] Content, string ContentType, string FileName);

/// <summary>
/// Provides profile photo operations for the current user. Implemented directly against
/// blob storage and the database on the server, and over HTTP in the WebAssembly client.
/// </summary>
public interface IProfilePhotoService
{
    /// <summary>
    /// Validates, processes, and stores the supplied profile photo for the current user,
    /// replacing any existing photo.
    /// </summary>
    /// <param name="upload">The cropped photo upload payload.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing <see cref="Success"/> on success,
    /// or a <see cref="ServiceProblem"/> on failure (validation or processing errors).
    /// </returns>
    Task<ServiceResult<Success>> SaveProfilePhotoAsync(ProfilePhotoUpload upload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user's saved profile photo information.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing <see cref="ProfilePhotoInfo"/> when the user has a saved photo;
    /// <see cref="ServiceProblem"/> with NotFound kind otherwise.
    /// </returns>
    Task<ServiceResult<ProfilePhotoInfo>> GetCurrentUserPhotoAsync(CancellationToken cancellationToken = default);
}
