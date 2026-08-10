using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Request input for paged campaign-participant roster queries.
/// </summary>
public sealed record GetCampaignParticipantRosterInput
{
    /// <summary>
    /// The default 1-based page number for roster queries.
    /// </summary>
    public const int DefaultPage = 1;

    /// <summary>
    /// The default page size for roster queries.
    /// </summary>
    public const int DefaultPageSize = 50;

    /// <summary>
    /// The maximum page size allowed for roster queries.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// The campaign identifier from the route.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }

    /// <summary>
    /// Optional case-insensitive search applied to the participant display name.
    /// </summary>
    [MaxLength(200)]
    public string? Search { get; init; }

    /// <summary>
    /// Optional graduation-year values to include via OR semantics.
    /// </summary>
    public IReadOnlyList<int>? GraduationYears { get; init; }

    /// <summary>
    /// Optional tag-definition identifiers to include via OR semantics.
    /// </summary>
    public IReadOnlyList<long>? TagDefinitionIds { get; init; }

    /// <summary>
    /// Optional placement-outcome filter. Allowed values: <c>undecided</c>, <c>assigned</c>,
    /// <c>notselected</c>, and <c>withdrawn</c>.
    /// </summary>
    [NotWhitespace, RegularExpression("(?i)^(undecided|assigned|notselected|withdrawn)$")]
    public string? Outcome { get; init; }

    /// <summary>
    /// Optional team identifier filter.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long? TeamId { get; init; }

    /// <summary>
    /// Optional sort field. Allowed values: <c>displayName</c>, <c>graduationYear</c>,
    /// <c>tryoutNumber</c>, <c>assignmentId</c>, <c>outcome</c>, and <c>teamName</c>.
    /// </summary>
    [NotWhitespace, RegularExpression("(?i)^(displayName|graduationYear|tryoutNumber|assignmentId|outcome|teamName)$")]
    public string? SortBy { get; init; }

    /// <summary>
    /// Optional sort direction. Allowed values: <c>asc</c> or <c>desc</c>.
    /// </summary>
    [NotWhitespace, RegularExpression("(?i)^(asc|desc)$")]
    public string? SortDirection { get; init; }

    /// <summary>
    /// The optional 1-based page number to return. The service applies <see cref="DefaultPage"/> when omitted.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? Page { get; init; } = DefaultPage;

    /// <summary>
    /// The optional page size. The service applies <see cref="DefaultPageSize"/> when omitted and clamps to <see cref="MaxPageSize"/>.
    /// </summary>
    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; } = DefaultPageSize;
}
