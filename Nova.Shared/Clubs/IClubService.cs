using Nova.Shared.Results;

namespace Nova.Shared.Clubs;

/// <summary>
/// Provides club management operations. Implemented server-side with direct database access
/// and client-side over HTTP for WebAssembly components.
/// </summary>
public interface IClubService
{
    /// <summary>
    /// Creates a new club and assigns the current user as its admin.
    /// </summary>
    /// <param name="input">The details for the new club.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing the created <see cref="ClubDto"/> on success,
    /// or a <see cref="ServiceProblem"/> on failure (validation errors, or the user already belongs to a club).
    /// </returns>
    Task<ServiceResult<ClubDto>> CreateClubAsync(CreateClubInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches for clubs whose name, city, or state contains the supplied query string.
    /// Returns all clubs when <paramref name="query"/> is null or empty.
    /// </summary>
    /// <param name="query">The search term, or <see langword="null"/> to return all clubs.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ServiceResult{TSuccess}"/> containing the matched clubs on success,
    /// or a <see cref="ServiceProblem"/> on unexpected failure.
    /// </returns>
    Task<ServiceResult<IReadOnlyList<ClubDto>>> SearchClubsAsync(string? query, CancellationToken cancellationToken = default);
}
