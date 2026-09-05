namespace Nova.Shared.Features.Clubs;

/// <summary>Represents the portable identity of the signed-in member's current club.</summary>
public sealed record ClubIdentityResult
{
    /// <summary>Gets the club identifier.</summary>
    public required long ClubId { get; init; }

    /// <summary>Gets the club display name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the city the club is based in.</summary>
    public required string City { get; init; }

    /// <summary>Gets the state the club is based in.</summary>
    public required string State { get; init; }

    /// <summary>Gets a value indicating whether the club has a crest image.</summary>
    public required bool HasCrest { get; init; }
}
