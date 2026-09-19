namespace Nova.SharedKernel.Features.Players;

/// <summary>
/// The club's manual-intake enrollment consequence as of one bounded read.
/// This is preview evidence only: an explicit null pair means no campaign was Active when the
/// context was read, and the committed operation receipt remains the authoritative enrollment result.
/// </summary>
public sealed record PlayerIntakeContext
{
    /// <summary>Gets the Active campaign's identifier, or null when the club has no Active campaign.</summary>
    public required long? CampaignId { get; init; }

    /// <summary>Gets the Active campaign's name, or null when the club has no Active campaign.</summary>
    public required string? CampaignName { get; init; }
}
