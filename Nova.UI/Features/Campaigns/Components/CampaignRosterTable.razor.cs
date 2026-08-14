using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
    /// Gets or sets the currently selected participant assignment identifier, or <see langword="null"/> when none is selected.
    /// </summary>
    [Parameter]
    public long? SelectedParticipantId { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a roster row is clicked or activated via keyboard.
    /// </summary>
    [Parameter]
    public EventCallback<CampaignParticipantRosterItem> OnParticipantSelected { get; set; }

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
    /// Forwards a row activation to the parent page.
    /// </summary>
    /// <param name="item">The activated roster item.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task SelectItemAsync(CampaignParticipantRosterItem item) => OnParticipantSelected.InvokeAsync(item);

    /// <summary>
    /// Activates a row when Enter or Space is pressed while it has focus.
    /// </summary>
    /// <remarks>
    /// The browser's default activation click for these keys is suppressed globally in
    /// <c>site.js</c>; otherwise the synthesized click would land on the drawer's close button
    /// (which receives focus when the drawer opens) and immediately close it.
    /// </remarks>
    /// <param name="args">The keyboard event.</param>
    /// <param name="item">The focused roster item.</param>
    /// <returns>A task that completes when the activation is forwarded.</returns>
    private Task OnRowKeyDownAsync(KeyboardEventArgs args, CampaignParticipantRosterItem item)
        => args.Key is "Enter" or " " ? SelectItemAsync(item) : Task.CompletedTask;

    /// <summary>
    /// Determines whether a roster item is the currently selected participant.
    /// </summary>
    /// <param name="item">The roster item to test.</param>
    /// <returns><see langword="true"/> when the item matches <see cref="SelectedParticipantId"/>.</returns>
    private bool IsSelected(CampaignParticipantRosterItem item)
        => SelectedParticipantId is not null && item.PlayerCampaignAssignmentId == SelectedParticipantId.Value;

    /// <summary>
    /// Computes the CSS class for a roster row.
    /// </summary>
    /// <param name="item">The roster item.</param>
    /// <returns>The row class, including a selected variant when applicable.</returns>
    private string RowClass(CampaignParticipantRosterItem item)
        => IsSelected(item) ? "roster-row roster-row-selected" : "roster-row";

    /// <summary>
    /// Computes the <c>aria-current</c> value for a roster row.
    /// </summary>
    /// <param name="item">The roster item.</param>
    /// <returns><c>true</c> when selected, otherwise <see langword="null"/> so the attribute is omitted.</returns>
    private string? AriaCurrent(CampaignParticipantRosterItem item)
        => IsSelected(item) ? "true" : null;

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
