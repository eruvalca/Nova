using Microsoft.AspNetCore.Components;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>Retains discovery ordering, result scale and paging beneath the bounded roster.</summary>
public partial class CampaignRosterFooter
{
    /// <summary>The current result count, separate from whole-campaign scale.</summary>
    [Parameter] public int? TotalCount { get; set; }
    /// <summary>Whether a replacement roster is loading.</summary>
    [Parameter] public bool IsLoading { get; set; }
    /// <summary>Whether filters can be cleared.</summary>
    [Parameter] public bool HasActiveFilters { get; set; }
    /// <summary>The requested ordering field.</summary>
    [Parameter] public string? SortBy { get; set; }
    /// <summary>The requested ordering direction.</summary>
    [Parameter] public string? SortDirection { get; set; }
    /// <summary>The current one-based page.</summary>
    [Parameter] public int Page { get; set; } = 1;
    /// <summary>The bounded page size.</summary>
    [Parameter] public int PageSize { get; set; } = 50;
    /// <summary>Changes the complete ordering pair.</summary>
    [Parameter] public EventCallback<string> OnOrderChanged { get; set; }
    /// <summary>Clears discovery filters.</summary>
    [Parameter] public EventCallback OnClearFilters { get; set; }
    /// <summary>Requests another result page.</summary>
    [Parameter] public EventCallback<int> OnPageChanged { get; set; }
    private string OrderValue => $"{SortBy ?? "displayName"}:{SortDirection ?? "asc"}";
}
