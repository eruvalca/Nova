using OneOf;

namespace Nova.Shared.Security;

/// <summary>
/// Represents an unauthenticated user.
/// </summary>
public sealed record Anonymous;

/// <summary>
/// Represents an authenticated user who does not belong to a club yet.
/// </summary>
/// <param name="UserId">The user's id.</param>
public sealed record AuthenticatedUser(long UserId);

/// <summary>
/// Represents an authenticated user who is a member of a club.
/// </summary>
/// <param name="UserId">The user's id.</param>
/// <param name="ClubId">The club (tenant) the user belongs to.</param>
/// <param name="IsClubAdmin">Whether the user holds the ClubAdmin role.</param>
public sealed record ClubMember(long UserId, long ClubId, bool IsClubAdmin);

/// <summary>
/// Discriminated union describing the current user's authentication/tenancy state.
/// Cases:
/// <list type="bullet">
/// <item><see cref="Anonymous"/> — no authenticated user.</item>
/// <item><see cref="AuthenticatedUser"/> — signed in, not yet a club member.</item>
/// <item><see cref="ClubMember"/> — signed in and belongs to a club.</item>
/// </list>
/// </summary>
[GenerateOneOf]
public partial class CurrentUserState : OneOfBase<Anonymous, AuthenticatedUser, ClubMember>
{
}
