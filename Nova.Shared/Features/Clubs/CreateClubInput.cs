using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Clubs;

/// <summary>
/// Input model for creating a new club.
/// </summary>
public sealed record CreateClubInput
{
    /// <summary>The display name for the new club.</summary>
    [Required, NotWhitespace, MaxLength(200)]
    public required string Name { get; init; }

    /// <summary>The city the club is based in.</summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string City { get; init; }

    /// <summary>The state the club is based in.</summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string State { get; init; }

    /// <summary>The raw bytes of the club crest image (JPEG, PNG, or WebP).</summary>
    [Required]
    public required byte[] CrestContent { get; init; }

    /// <summary>The content type of the club crest image, as declared by the client.</summary>
    [Required, NotWhitespace]
    public required string CrestContentType { get; init; }
}
