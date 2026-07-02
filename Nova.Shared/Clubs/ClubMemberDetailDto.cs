namespace Nova.Shared.Clubs;

/// <summary>
/// Represents a club member in the full club roster for the admin page.
/// </summary>
/// <param name="UserId">The member's user identifier.</param>
/// <param name="FullName">The member's full display name.</param>
/// <param name="IsClubAdmin"><see langword="true"/> when the member is a club administrator.</param>
/// <param name="IsCurrentUser"><see langword="true"/> when this member is the signed-in user.</param>
public sealed record ClubMemberDetailDto(long UserId, string FullName, bool IsClubAdmin, bool IsCurrentUser);
