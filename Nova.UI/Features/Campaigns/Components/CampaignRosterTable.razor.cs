using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the desktop campaign roster table with sortable headers.
/// </summary>
public partial class CampaignRosterTable
{
    /// <summary>
    /// Gets or sets the roster rows to display.
    /// </summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<CampaignParticipantRosterItem> Items { get; set; } = [];

    /// <summary>
    /// Gets or sets the active sort field token, or <see langword="null"/> when the server default applies.
    /// </summary>
    [Parameter]
    public string? SortBy { get; set; }

    /// <summary>
    /// Gets or sets the active sort direction token (<c>asc</c> or <c>desc</c>).
    /// </summary>
    [Parameter]
    public string? SortDirection { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a sortable header is clicked, with the requested sort field.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnSortChanged { get; set; }

    /// <summary>
    /// Forwards a header click to the parent page.
    /// </summary>
    /// <param name="column">The clicked sort field token.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task ToggleSortAsync(string column) => OnSortChanged.InvokeAsync(column);

    /// <summary>
    /// Computes the <c>aria-sort</c> value for a column header.
    /// </summary>
    /// <param name="column">The column's sort field token.</param>
    /// <returns><c>ascending</c>, <c>descending</c>, or <c>none</c>.</returns>
    private string AriaSort(string column)
    {
        if (!string.Equals(SortBy, column, StringComparison.OrdinalIgnoreCase))
        {
            return "none";
        }

        return string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
            ? "descending"
            : "ascending";
    }

    /// <summary>
    /// Computes the decorative sort arrow for a column header.
    /// </summary>
    /// <param name="column">The column's sort field token.</param>
    /// <returns>The arrow character, or an empty string when the column is not sorted.</returns>
    private string SortArrow(string column)
    {
        if (!string.Equals(SortBy, column, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return string.Equals(SortDirection, "desc", StringComparison.OrdinalIgnoreCase)
            ? "↓"
            : "↑";
    }
}
