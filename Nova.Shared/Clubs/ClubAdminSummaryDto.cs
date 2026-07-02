namespace Nova.Shared.Clubs;

/// <summary>
/// Represents the club-admin summary payload for the club overview card.
/// </summary>
/// <param name="ClubId">The unique identifier of the club.</param>
/// <param name="Name">The club's display name.</param>
/// <param name="City">The city the club is based in.</param>
/// <param name="State">The state the club is based in.</param>
/// <param name="MemberCount">The total number of members in the club.</param>
/// <param name="AdminCount">The number of club administrators for the club.</param>
/// <param name="PendingJoinRequestCount">The number of pending join requests for the club.</param>
/// <param name="PlayerCount">The number of players associated with the club.</param>
/// <param name="IsCurrentUserSoleAdmin"><see langword="true"/> when the signed-in user is the only club administrator.</param>
public sealed record ClubAdminSummaryDto(
    long ClubId,
    string Name,
    string City,
    string State,
    int MemberCount,
    int AdminCount,
    int PendingJoinRequestCount,
    int PlayerCount,
    bool IsCurrentUserSoleAdmin);
