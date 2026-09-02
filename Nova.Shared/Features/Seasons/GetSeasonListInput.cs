using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Seasons;

/// <summary>Describes a bounded season-list request.</summary>
public sealed record GetSeasonListInput
{
    /// <summary>Gets the default page number.</summary>
    public const int DefaultPage = 1;

    /// <summary>Gets the default page size.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Gets the maximum page size.</summary>
    public const int MaximumPageSize = 50;

    /// <summary>Gets the one-based page number.</summary>
    [Range(1, int.MaxValue)]
    public int? Page { get; init; }

    /// <summary>Gets the number of seasons returned per page.</summary>
    [Range(1, MaximumPageSize)]
    public int? PageSize { get; init; }
}
