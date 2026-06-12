namespace Nova.Shared.Clubs;

/// <summary>
/// Represents a club returned from a service operation.
/// </summary>
/// <param name="ClubId">The unique identifier of the club.</param>
/// <param name="Name">The club's display name.</param>
/// <param name="City">The city the club is based in.</param>
/// <param name="State">The state the club is based in.</param>
public sealed record ClubDto(long ClubId, string Name, string City, string State);
