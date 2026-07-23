using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Players;

/// <summary>
/// Query input for retrieving a club's player roster.
/// </summary>
public sealed record GetPlayerRosterInput
{
    /// <summary>
    /// The default 1-based page number for roster queries.
    /// </summary>
    public const int DefaultPage = 1;

    /// <summary>
    /// The default page size for roster queries.
    /// </summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// The maximum page size allowed for roster queries.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// The club identifier from the route.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long ClubId { get; init; }

    /// <summary>
    /// Optional case-insensitive search term applied to player display names.
    /// </summary>
    [MaxLength(200)]
    public string? Search { get; init; }

    /// <summary>
    /// Optional sort field. Allowed values: <c>displayName</c>, <c>joinedAt</c>.
    /// </summary>
    [RegularExpression("(?i)^(displayName|joinedAt)$")]
    public string? SortBy { get; init; }

    /// <summary>
    /// Optional sort direction. Allowed values: <c>asc</c>, <c>desc</c>.
    /// </summary>
    [RegularExpression("(?i)^(asc|desc)$")]
    public string? SortDirection { get; init; }

    /// <summary>
    /// The optional 1-based page number to return. The service applies <see cref="DefaultPage"/> when omitted.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? Page { get; init; } = DefaultPage;

    /// <summary>
    /// The optional number of results per page. The service applies <see cref="DefaultPageSize"/> when omitted.
    /// </summary>
    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; } = DefaultPageSize;
}
