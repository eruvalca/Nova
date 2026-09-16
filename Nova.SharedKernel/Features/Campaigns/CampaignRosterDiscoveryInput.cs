using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Validation;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Campaign-local discovery shared by Active work and immutable Closed records.</summary>
public abstract record CampaignRosterDiscoveryInput : PlacementPageInput
{
    /// <summary>The campaign to read.</summary>
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }

    /// <summary>A literal name substring or exact tryout number.</summary>
    [MaxLength(200)]
    public string? Search { get; init; }

#pragma warning disable CA1819 // Minimal API binds repeated scalar query parameters as arrays.
    /// <summary>Graduation years combined with OR; other filter groups use AND.</summary>
    public int[]? GraduationYears { get; init; }

    /// <summary>Campaign-applied tag identifiers combined with OR.</summary>
    public long[]? TagDefinitionIds { get; init; }
#pragma warning restore CA1819

    /// <summary>A campaign-local outcome, independent of effective season placement.</summary>
    [NotWhitespace, RegularExpression("(?i)^(undecided|assigned|notselected|withdrawn)$")]
    public string? LocalOutcome { get; init; }

    /// <summary>A campaign-local team, independent of the effective-team filter.</summary>
    [Range(1, long.MaxValue)]
    public long? LocalTeamId { get; init; }

    /// <summary>An exact participant-assignment identifier for bounded linked context.</summary>
    [Range(1, long.MaxValue)]
    public long? ParticipantId { get; init; }

    /// <summary>The roster sort; a direction alone uses display name. Omitting both fields preserves the read's default.</summary>
    [NotWhitespace, RegularExpression("(?i)^(displayName|graduationYear|tryoutNumber|assignmentId|outcome|teamName|searchRelevance|closeout)$")]
    public string? SortBy { get; init; }

    /// <summary>Ascending or descending primary order; ties remain deterministic.</summary>
    [NotWhitespace, RegularExpression("(?i)^(asc|desc)$")]
    public string? SortDirection { get; init; }

    /// <inheritdoc />
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var error in base.Validate(validationContext))
        {
            yield return error;
        }
        if (string.Equals(SortBy, "closeout", StringComparison.OrdinalIgnoreCase)
            && string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult("Closeout groups use ascending review order.", [nameof(SortDirection)]);
        }
        if (GraduationYears?.Any(year => year <= 0) == true)
        {
            yield return new ValidationResult("Graduation years must be positive.", [nameof(GraduationYears)]);
        }
        if (TagDefinitionIds?.Any(id => id <= 0) == true)
        {
            yield return new ValidationResult("Tag identifiers must be positive.", [nameof(TagDefinitionIds)]);
        }
    }
}
