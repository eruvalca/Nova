using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Nova.Shared.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the narrow-screen campaign roster card list.
/// </summary>
public partial class CampaignRosterCards
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
    /// Gets or sets the callback invoked when a roster card is clicked or activated via keyboard.
    /// </summary>
    [Parameter]
    public EventCallback<CampaignParticipantRosterItem> OnParticipantSelected { get; set; }

    /// <summary>
    /// Forwards a card activation to the parent page.
    /// </summary>
    /// <param name="item">The activated roster item.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task SelectItemAsync(CampaignParticipantRosterItem item) => OnParticipantSelected.InvokeAsync(item);

    /// <summary>
    /// Activates a card when Enter or Space is pressed while it has focus.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    /// <param name="item">The focused roster item.</param>
    /// <returns>A task that completes when the activation is forwarded.</returns>
    private Task OnCardKeyDownAsync(KeyboardEventArgs args, CampaignParticipantRosterItem item)
        => args.Key is "Enter" or " " ? SelectItemAsync(item) : Task.CompletedTask;

    /// <summary>
    /// Determines whether a roster item is the currently selected participant.
    /// </summary>
    /// <param name="item">The roster item to test.</param>
    /// <returns><see langword="true"/> when the item matches <see cref="SelectedParticipantId"/>.</returns>
    private bool IsSelected(CampaignParticipantRosterItem item)
        => SelectedParticipantId is not null && item.PlayerCampaignAssignmentId == SelectedParticipantId.Value;

    /// <summary>
    /// Computes the CSS class for a roster card.
    /// </summary>
    /// <param name="item">The roster item.</param>
    /// <returns>The card class, including a selected variant when applicable.</returns>
    private string CardClass(CampaignParticipantRosterItem item)
        => IsSelected(item) ? "list-group-item roster-card roster-card-selected" : "list-group-item roster-card";

    /// <summary>
    /// Computes the <c>aria-current</c> value for a roster card.
    /// </summary>
    /// <param name="item">The roster item.</param>
    /// <returns><c>true</c> when selected, otherwise <see langword="null"/> so the attribute is omitted.</returns>
    private string? AriaCurrent(CampaignParticipantRosterItem item)
        => IsSelected(item) ? "true" : null;
}
