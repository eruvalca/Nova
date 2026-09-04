using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Nova.Data.Tenancy;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Photos;
using Nova.Shared.Security;

namespace Nova.Components.Layout;

/// <summary>
/// Renders the primary application navigation bar and tracks the current URL for logout return behavior.
/// </summary>
public partial class NavMenu(
    NavigationManager navigationManager,
    ICurrentUserProvider currentUserProvider,
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider serviceProvider)
{
    /// <summary>
    /// Gets a value indicating whether the account routes list carries exactly one item —
    /// the anonymous single-Login case. The anonymous branch is known server-side (there is
    /// exactly one account route: Login), so instead of a client-side <c>:has()</c> selector
    /// (which JS-on browsers without <c>:has()</c> support would drop) the state is computed
    /// here and emitted as the <c>account-routes-single</c> marker class on the nav element.
    /// </summary>
    protected bool IsAccountRoutesSingle => currentUserProvider.GetCurrentUserState().Value is Anonymous;

    /// <summary>
    /// Stores the current base-relative URL used as the post-logout return URL.
    /// </summary>
    private string? currentUrl;

    /// <summary>
    /// Gets the current base-relative URL used in the logout form.
    /// </summary>
    protected string? CurrentUrl => currentUrl;

    /// <summary>
    /// Gets a value indicating whether the current route is inside the club area but not the
    /// Teams subsection, so the Club link stays active on every club route except the Teams
    /// routes that carry their own link.
    /// </summary>
    protected bool ClubSectionActive => IsClubActive(currentUrl);

    /// <summary>
    /// Determines whether a URL activates the Club link, including legacy numeric club routes
    /// and excluding the Teams subsection, which has its own navigation item.
    /// </summary>
    /// <param name="baseRelativeUrl">The base-relative URL, optionally including a query or fragment, or null.</param>
    /// <returns>True for a Club destination outside Teams; otherwise false.</returns>
    private static bool IsClubActive(string? baseRelativeUrl)
    {
        if (baseRelativeUrl is null)
        {
            return false;
        }

        var path = baseRelativeUrl.Split('?', '#')[0].TrimStart('/').TrimEnd('/');
        if (path.Length == 0)
        {
            return false;
        }

        var isClubArea = path.Equals("club", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("club/", StringComparison.OrdinalIgnoreCase)
            || IsLegacyClubRoute(path);
        var isTeamsArea = path.Equals("club/teams", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("club/teams/", StringComparison.OrdinalIgnoreCase);
        return isClubArea && !isTeamsArea;
    }

    /// <summary>
    /// Recognizes legacy pre-shell club routes (<c>Clubs/{clubId}</c> and
    /// <c>Clubs/{clubId}/admin</c>) so the Club link stays active there too. The numeric
    /// route constraint excludes <c>Clubs/Onboarding</c>, which is not part of the club area.
    /// </summary>
    private static bool IsLegacyClubRoute(string path)
    {
        const string prefix = "clubs/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var heading = path.AsSpan(prefix.Length);
        var firstSlash = heading.IndexOf('/');
        var clubId = firstSlash < 0 ? heading : heading[..firstSlash];
        if (clubId.Length == 0 || !long.TryParse(clubId, out _))
        {
            return false;
        }

        // Allow both Clubs/{clubId} and Clubs/{clubId}/admin; anything deeper (e.g. a
        // potential future Clubs/{clubId}/something-else) is outside the club shell area's
        // known surface, so it stays inactive rather than guessing.
        return firstSlash < 0 || heading.Slice(firstSlash).Equals("/admin", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the URL for the current user's small profile photo, or null if the user has no photo.
    /// </summary>
    protected string? PhotoUrl => currentUserProvider.UserId.HasValue
        ? PhotoEndpoints.GetPhotoUrl(currentUserProvider.UserId.Value, ProfilePhotoSize.Small)
        : null;

    /// <summary>
    /// Gets the canonical URL for the current user's club, or null if the user has no club.
    /// </summary>
    protected string? ClubDetailUrl => currentUserProvider.ClubId.HasValue
        ? ClubRoutes.Overview
        : null;

    /// <summary>
    /// Gets the current user's club display name from the principal claims, or null if the user has no club.
    /// </summary>
    protected string? ClubName => currentUserProvider.ClubId.HasValue
        ? GetPrincipal()?.FindFirstValue(NovaClaimTypes.ClubName)
        : null;

    /// <summary>
    /// Gets the URL for the current user's club crest (small variant), or null if the user has no club.
    /// </summary>
    protected string? ClubCrestUrl => currentUserProvider.ClubId.HasValue
        ? ClubCrestEndpoints.GetCrestUrl(currentUserProvider.ClubId.Value, ProfilePhotoSize.Small)
        : null;

    private ClaimsPrincipal? GetPrincipal()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            return httpContext.User;
        }

        if (serviceProvider.GetService<AuthenticationStateProvider>() is { } authenticationStateProvider)
        {
            try
            {
                var task = authenticationStateProvider.GetAuthenticationStateAsync();
                var state = task.IsCompletedSuccessfully ? task.Result : task.GetAwaiter().GetResult();
                return state.User;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return null;
    }

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
