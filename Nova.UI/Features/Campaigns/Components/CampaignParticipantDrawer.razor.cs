using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Nova.Shared.Features.Campaigns;
using Nova.UI.Components;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Drawer shell for the selected campaign roster participant. This phase renders the shell only;
/// issue #64 fills in the participant-detail body.
/// </summary>
/// <param name="jsRuntime">The JavaScript runtime used to focus the close button when the drawer opens.</param>
public partial class CampaignParticipantDrawer(IJSRuntime jsRuntime) : NovaComponentBase
{
    /// <summary>
    /// The DOM identifier of the close button, focused when the drawer opens.
    /// </summary>
    private const string CloseButtonId = "participant-drawer-close";

    /// <summary>
    /// Gets or sets the selected participant assignment identifier.
    /// </summary>
    [Parameter, EditorRequired]
    public long ParticipantId { get; set; }

    /// <summary>
    /// Gets or sets the selected roster item, or <see langword="null"/> when it is not on the loaded page.
    /// </summary>
    [Parameter]
    public CampaignParticipantRosterItem? RosterItem { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the drawer is closed.
    /// </summary>
    [Parameter]
    public EventCallback OnClose { get; set; }

    /// <summary>
    /// Closes the drawer via the parent page.
    /// </summary>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task CloseAsync() => OnClose.InvokeAsync();

    /// <summary>
    /// Closes the drawer when Escape is pressed inside the panel.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnKeyDownAsync(KeyboardEventArgs args)
        => string.Equals(args.Key, "Escape", StringComparison.OrdinalIgnoreCase)
            ? CloseAsync()
            : Task.CompletedTask;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await jsRuntime.InvokeVoidAsync("novaCampaignWorkspaceFocus", CloseButtonId);
        }
    }
}
