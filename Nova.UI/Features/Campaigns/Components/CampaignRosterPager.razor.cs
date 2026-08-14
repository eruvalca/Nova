using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Campaigns;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the bounded roster pager driven by the total match count and fixed page size.
/// </summary>
public partial class CampaignRosterPager
{
    /// <summary>
    /// Gets or sets the one-based current page.
    /// </summary>
    [Parameter]
    public int Page { get; set; } = GetCampaignParticipantRosterInput.DefaultPage;

    /// <summary>
    /// Gets or sets the fixed page size.
    /// </summary>
    [Parameter]
    public int PageSize { get; set; } = GetCampaignParticipantRosterInput.DefaultPageSize;

    /// <summary>
    /// Gets or sets the total matching row count.
    /// </summary>
    [Parameter]
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a pager button is clicked, with the requested page.
    /// </summary>
    [Parameter]
    public EventCallback<int> OnPageChanged { get; set; }

    /// <summary>
    /// Gets the total number of pages, never less than one.
    /// </summary>
    private int TotalPages => CampaignWorkspaceUrlState.CalculatePageCount(TotalCount, PageSize);

    /// <summary>
    /// Forwards a pager click to the parent page.
    /// </summary>
    /// <param name="page">The requested page.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task GoToPageAsync(int page) => OnPageChanged.InvokeAsync(page);
}
