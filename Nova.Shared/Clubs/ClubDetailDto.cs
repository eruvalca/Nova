using Nova.Shared.Account;

namespace Nova.Shared.Clubs;

/// <summary>
/// Represents the club-detail payload for the club detail page.
/// </summary>
/// <param name="ClubId">The unique identifier of the club.</param>
/// <param name="Name">The club's display name.</param>
/// <param name="City">The city the club is based in.</param>
/// <param name="State">The state the club is based in.</param>
/// <param name="Members">The current member roster, including the signed-in user.</param>
/// <param name="IsCurrentUserClubAdmin"><see langword="true"/> when the signed-in user is the club's ClubAdmin.</param>
public sealed record ClubDetailDto(
    long ClubId,
    string Name,
    string City,
    string State,
    IReadOnlyList<ClubRosterMemberDto> Members,
    bool IsCurrentUserClubAdmin);
