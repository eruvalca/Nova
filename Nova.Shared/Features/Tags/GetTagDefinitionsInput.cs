using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Tags;

/// <summary>
/// Defines the optional filters for the current club's tag-definition management list.
/// </summary>
public sealed record GetTagDefinitionsInput
{
    /// <summary>
    /// Gets the optional case-insensitive tag-definition-name search term.
    /// </summary>
    [MaxLength(TagDefinitionLimits.MaxSearchLength)]
    public string? Search { get; init; }

    /// <summary>
    /// Gets the optional lifecycle view, which accepts <c>active</c>, <c>archived</c>, or <c>all</c>.
    /// </summary>
    [NotWhitespace, RegularExpression("(?i)^(active|archived|all)$")]
    public string? LifecycleStatus { get; init; }
}
