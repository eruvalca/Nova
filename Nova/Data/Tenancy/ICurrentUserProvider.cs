namespace Nova.Data.Tenancy;

using Nova.Shared.Security;

/// <summary>
/// Provides information about the current authenticated user for tenancy enforcement.
/// </summary>
public interface ICurrentUserProvider
{
    /// <summary>
    /// Gets the current user's id, or null when unauthenticated.
    /// Kept as a flat primitive so EF query filters can parameterize it.
    /// </summary>
    long? UserId { get; }

    /// <summary>
    /// Gets the current user's club (tenant) id, or null when the user has no club.
    /// Kept as a flat primitive so EF query filters can parameterize it.
    /// </summary>
    long? ClubId { get; }

    /// <summary>
    /// Gets a value indicating whether the current user is in the ClubAdmin role.
    /// Kept as a flat primitive so EF query filters can parameterize it.
    /// </summary>
    bool IsClubAdmin { get; }

    /// <summary>
    /// Gets the current user's state as a discriminated union for exhaustive handling in
    /// application code. Cases: <see cref="Anonymous"/>, <see cref="AuthenticatedUser"/>,
    /// or <see cref="ClubMember"/>.
    /// </summary>
    /// <returns>The current user state.</returns>
    CurrentUserState GetCurrentUserState();
}
