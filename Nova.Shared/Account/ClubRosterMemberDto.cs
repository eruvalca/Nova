namespace Nova.Shared.Account;

/// <summary>
/// Represents a club member in the club-detail roster.
/// </summary>
/// <param name="UserId">The member's user identifier.</param>
/// <param name="FullName">The member's display name.</param>
/// <param name="IsCurrentUser"><see langword="true"/> when this member is the signed-in user viewing the page.</param>
public sealed record ClubRosterMemberDto(long UserId, string FullName, bool IsCurrentUser);
