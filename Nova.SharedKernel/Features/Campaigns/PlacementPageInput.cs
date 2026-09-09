using System.ComponentModel.DataAnnotations;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Shared bounded placement paging; totals may change between page requests.</summary>
public abstract record PlacementPageInput : IValidatableObject
{
    /// <summary>The default number of rows.</summary>
    public const int DefaultPageSize = 50;
    /// <summary>The largest permitted page.</summary>
    public const int MaximumPageSize = 100;
    /// <summary>The optional one-based page; defaults to one.</summary>
    [Range(1, int.MaxValue)]
    public int? Page { get; init; }
    /// <summary>The optional page size; defaults to fifty.</summary>
    [Range(1, MaximumPageSize)]
    public int? PageSize { get; init; }

    /// <inheritdoc />
    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if ((long)((Page ?? 1) - 1) * (PageSize ?? DefaultPageSize) > int.MaxValue)
        {
            yield return new ValidationResult("The requested page is too large.", [nameof(Page)]);
        }
    }
}
