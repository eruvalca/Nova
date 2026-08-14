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
/// <param name="jsRuntime">The JavaScript runtime used to import the collocated drawer module.</param>
public partial class CampaignParticipantDrawer(IJSRuntime jsRuntime) : NovaComponentBase
{
    /// <summary>
    /// The close button element, focused when the drawer opens.
    /// </summary>
    private ElementReference _closeButton;

    /// <summary>
    /// The lazily imported collocated drawer module used to focus the close button.
    /// </summary>
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask = new(() => jsRuntime
        .InvokeAsync<IJSObjectReference>(
            "import", "./_content/Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.js")
        .AsTask());

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
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("focus", _closeButton);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        if (_moduleTask.IsValueCreated)
        {
            var module = await _moduleTask.Value;
            await module.DisposeAsync();
        }
    }
}
