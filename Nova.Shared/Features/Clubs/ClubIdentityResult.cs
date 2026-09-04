namespace Nova.Shared.Features.Clubs;

/// <summary>Represents the portable identity of the signed-in member's current club.</summary>
public sealed record ClubIdentityResult
{
    public required long ClubId { get; init; }
    public required string Name { get; init; }
    public required string City { get; init; }
    public required string State { get; init; }
    public required bool HasCrest { get; init; }
}
