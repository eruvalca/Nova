using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Nova.Data.Tenancy;
using Nova.Shared.Photos;

namespace Nova.Components.Layout;

/// <summary>
/// Renders the primary application navigation bar and tracks the current URL for logout return behavior.
/// </summary>
public partial class NavMenu(NavigationManager navigationManager, ICurrentUserProvider currentUserProvider)
{
    /// <summary>
    /// Stores the current base-relative URL used as the post-logout return URL.
    /// </summary>
    private string? currentUrl;

    /// <summary>
    /// Gets the current base-relative URL used in the logout form.
    /// </summary>
    protected string? CurrentUrl => currentUrl;

    /// <summary>
    /// Gets the URL for the current user's small profile photo, or null if the user has no photo.
    /// </summary>
    protected string? PhotoUrl => currentUserProvider.UserId.HasValue
        ? PhotoEndpoints.GetPhotoUrl(currentUserProvider.UserId.Value, ProfilePhotoSize.Small)
        : null;

    /// <summary>
    /// Subscribes to location change notifications and initializes the current URL value.
    /// </summary>
    protected override void OnInitialized()
    {
        currentUrl = navigationManager.ToBaseRelativePath(navigationManager.Uri);
        navigationManager.LocationChanged += OnLocationChanged;
    }

    /// <summary>
    /// Updates the current URL whenever navigation changes.
    /// </summary>
    /// <param name="sender">The location change event source.</param>
    /// <param name="e">The location change event payload.</param>
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        currentUrl = navigationManager.ToBaseRelativePath(e.Location);
        StateHasChanged();
    }

    /// <summary>
    /// Unsubscribes from location change notifications during component disposal.
    /// </summary>
    /// <returns>A completed task value.</returns>
    protected override ValueTask DisposeAsyncCore()
    {
        navigationManager.LocationChanged -= OnLocationChanged;
        return ValueTask.CompletedTask;
    }
}
