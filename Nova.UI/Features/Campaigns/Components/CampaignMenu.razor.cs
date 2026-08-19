using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Nova.UI.Components;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders a native, keyboard-operable campaign action menu for club administrators. It is a pure
/// Blazor disclosure (no JavaScript), so the items are limited to the administrator-only metadata and
/// lifecycle actions.
/// </summary>
public partial class CampaignMenu : NovaComponentBase
{
    /// <summary>
    /// Gets or sets whether the current user holds the club administrator role.
    /// </summary>
    [Parameter]
    public bool IsClubAdmin { get; set; }

    /// <summary>
    /// Gets or sets whether the campaign is closed.
    /// </summary>
    [Parameter]
    public bool IsClosed { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the administrator selects Edit metadata.
    /// </summary>
    [Parameter]
    public EventCallback OnEditMetadata { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the administrator selects Close campaign.
    /// </summary>
    [Parameter]
    public EventCallback OnCloseCampaign { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the administrator selects Reopen.
    /// </summary>
    [Parameter]
    public EventCallback OnReopen { get; set; }

    /// <summary>
    /// Indicates whether the menu disclosure is open.
    /// </summary>
    private bool _isOpen;

    /// <summary>
    /// Toggles the menu disclosure open or closed.
    /// </summary>
    private void ToggleMenu() => _isOpen = !_isOpen;

    /// <summary>
    /// Closes the menu when Escape is pressed.
    /// </summary>
    /// <param name="args">The keyboard event arguments.</param>
    private void OnKeyDown(KeyboardEventArgs args)
    {
        if (string.Equals(args.Key, "Escape", StringComparison.OrdinalIgnoreCase))
        {
            _isOpen = false;
        }
    }

    /// <summary>
    /// Closes the menu and invokes the selected item's callback.
    /// </summary>
    /// <param name="callback">The callback for the selected menu item.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private async Task SelectItemAsync(EventCallback callback)
    {
        _isOpen = false;
        await callback.InvokeAsync();
    }
}
