namespace Nova.Shared.Account;

/// <summary>A member of a club, used when selecting a new ClubAdmin.</summary>
/// <param name="UserId">The member's user identifier.</param>
/// <param name="FullName">The member's display name.</param>
public sealed record ClubMemberDto(long UserId, string FullName);
