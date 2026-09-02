using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Activity;

/// <summary>
/// Request input for the keyset-paged club activity feed. The two cursor properties are supplied
/// together by the client from <see cref="ClubActivityCursor"/>; supplying neither returns the
/// newest page.
/// </summary>
public sealed record GetClubActivityInput : IValidatableObject
{
    /// <summary>
    /// The fixed page size of the club activity feed.
    /// </summary>
    public const int PageSize = 20;

    /// <summary>
    /// Gets the exclusive lower bound on the activity event identifier of returned rows. Must be
    /// supplied together with <see cref="BeforeOccurredAt"/>.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long? BeforeActivityEventId { get; init; }

    /// <summary>
    /// Gets the exclusive upper bound on the occurrence time of returned rows. Must be supplied
    /// together with <see cref="BeforeActivityEventId"/>.
    /// </summary>
    public DateTimeOffset? BeforeOccurredAt { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BeforeActivityEventId is null != BeforeOccurredAt is null)
        {
            yield return new ValidationResult(
                "Both the cursor activity event identifier and cursor occurrence time must be supplied together.",
                [nameof(BeforeActivityEventId), nameof(BeforeOccurredAt)]);
        }
    }
}
