using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account;

/// <summary>
/// Manages redirects and status message cookies for Identity operations.
/// </summary>
public sealed class IdentityRedirectManager(NavigationManager navigationManager)
{
    /// <summary>
    /// Gets the name of the cookie used to store Identity operation status messages.
    /// </summary>
    public const string StatusCookieName = "Identity.StatusMessage";

    /// <summary>
    /// Gets the configured cookie builder for Identity status messages with strict security settings.
    /// </summary>
    private static readonly CookieBuilder StatusCookieBuilder = new()
    {
        SameSite = SameSiteMode.Strict,
        HttpOnly = true,
        IsEssential = true,
        MaxAge = TimeSpan.FromSeconds(5),
    };

    /// <summary>
    /// Redirects to the specified URI after normalizing and validating it to prevent open redirects.
    /// </summary>
    /// <param name="uri">The target URI, or <see langword="null"/> to navigate to an empty path.</param>
    public void RedirectTo(string? uri)
    {
        uri ??= "";

        // Prevent open redirects.
        if (!Uri.IsWellFormedUriString(uri, UriKind.Relative))
        {
            uri = navigationManager.ToBaseRelativePath(uri);
        }

        navigationManager.NavigateTo(uri);
    }

    /// <summary>
    /// Redirects to the specified URI with query parameters appended.
    /// </summary>
    /// <param name="uri">The target URI.</param>
    /// <param name="queryParameters">A dictionary of query parameters to append to the URI.</param>
    public void RedirectTo(string uri, Dictionary<string, object?> queryParameters)
    {
        var uriWithoutQuery = navigationManager.ToAbsoluteUri(uri).GetLeftPart(UriPartial.Path);
        var newUri = navigationManager.GetUriWithQueryParameters(uriWithoutQuery, queryParameters);
        RedirectTo(newUri);
    }

    /// <summary>
    /// Redirects to the specified URI after storing a status message in a cookie.
    /// </summary>
    /// <param name="uri">The target URI.</param>
    /// <param name="message">The status message to store in the cookie.</param>
    /// <param name="context">The current HTTP context.</param>
    public void RedirectToWithStatus(string uri, string message, HttpContext context)
    {
        context.Response.Cookies.Append(StatusCookieName, message, StatusCookieBuilder.Build(context));
        RedirectTo(uri);
    }

    /// <summary>
    /// Gets the current absolute path of the application (without query string or fragment).
    /// </summary>
    private string CurrentPath => navigationManager.ToAbsoluteUri(navigationManager.Uri).GetLeftPart(UriPartial.Path);

    /// <summary>
    /// Redirects to the current page.
    /// </summary>
    public void RedirectToCurrentPage() => RedirectTo(CurrentPath);

    /// <summary>
    /// Redirects to the current page after storing a status message in a cookie.
    /// </summary>
    /// <param name="message">The status message to store in the cookie.</param>
    /// <param name="context">The current HTTP context.</param>
    public void RedirectToCurrentPageWithStatus(string message, HttpContext context)
        => RedirectToWithStatus(CurrentPath, message, context);

    /// <summary>
    /// Redirects to the InvalidUser page with an error message when the user cannot be loaded.
    /// </summary>
    /// <param name="userManager">The user manager service.</param>
    /// <param name="context">The current HTTP context.</param>
    public void RedirectToInvalidUser(UserManager<NovaUserEntity> userManager, HttpContext context)
        => RedirectToWithStatus("Account/InvalidUser", $"Error: Unable to load user with ID '{userManager.GetUserId(context.User)}'.", context);
}
