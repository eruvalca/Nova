using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Request input for the bounded, filtered placement roster of a campaign.
/// </summary>
public sealed record GetCampaignPlacementRosterInput
{
    /// <summary>
    /// The default 1-based page number for placement roster queries.
    /// </summary>
    public const int DefaultPage = 1;

    /// <summary>
    /// The default page size for placement roster queries.
    /// </summary>
    public const int DefaultPageSize = 50;

    /// <summary>
    /// The maximum page size allowed for placement roster queries.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// The campaign identifier from the route.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }

    /// <summary>
    /// Optional exact graduation-year filter. Omission matches every graduation year.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? GraduationYear { get; init; }

    /// <summary>
    /// Optional filter restricting rows to unresolved placements. When <see langword="true"/>,
    /// only rows whose outcome is <c>Undecided</c> are returned.
    /// </summary>
    public bool? UnresolvedOnly { get; init; }

    /// <summary>
    /// The optional 1-based page number to return. The service applies <see cref="DefaultPage"/> when omitted.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? Page { get; init; } = DefaultPage;

    /// <summary>
    /// The optional page size. The service applies <see cref="DefaultPageSize"/> when omitted, and validation rejects values above <see cref="MaxPageSize"/>.
    /// </summary>
    [Range(1, MaxPageSize)]
    public int? PageSize { get; init; } = DefaultPageSize;
}
