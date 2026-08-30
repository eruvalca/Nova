using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace Nova.Components.Account.Shared;

/// <summary>
/// Provides the shared static-SSR shell and route-aware directory for account access pages.
/// </summary>
public partial class AuthLayout(NavigationManager navigationManager)
{
    /// <summary>
    /// Gets the account directory panels, preserving a safe local continuation when switching
    /// between sign-in and registration.
    /// </summary>
    private AuthPanel[] Panels =>
    [
        new(AuthArea.SignIn, "Sign in", BuildEntryHref("Account/Login")),
        new(AuthArea.Register, "Register", BuildEntryHref("Account/Register")),
        new(AuthArea.Recover, "Recover access", "Account/ForgotPassword"),
        new(AuthArea.Manage, "Manage profile", "Account/Manage"),
    ];

    /// <summary>
    /// Gets the account area that owns the current route, or <see langword="null"/> when the route
    /// is a cross-cutting status surface.
    /// </summary>
    private AuthArea? CurrentArea => ResolveArea(navigationManager.ToBaseRelativePath(navigationManager.Uri));

    /// <summary>
    /// Gets the CSS classes for a directory panel.
    /// </summary>
    /// <param name="area">The area represented by the panel.</param>
    /// <returns>The panel classes, including the active class when appropriate.</returns>
    private string GetPanelClass(AuthArea area) => IsActive(area) ? "nav-link active" : "nav-link";

    /// <summary>
    /// Determines whether a directory panel owns the current route.
    /// </summary>
    /// <param name="area">The area represented by the panel.</param>
    /// <returns><see langword="true"/> when the panel owns the current route.</returns>
    private bool IsActive(AuthArea area) => CurrentArea == area;

    /// <summary>
    /// Resolves an account route to the directory area that owns it.
    /// </summary>
    /// <param name="relativePath">The current application-relative path.</param>
    /// <returns>The owning area, or <see langword="null"/> for unowned status routes.</returns>
    private static AuthArea? ResolveArea(string relativePath)
    {
        var path = relativePath.Split('?', '#')[0].TrimEnd('/');

        if (path.EndsWith("ConfirmEmailChange", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("Account/Manage", StringComparison.OrdinalIgnoreCase))
        {
            return AuthArea.Manage;
        }

        if (path.EndsWith("Login", StringComparison.OrdinalIgnoreCase)
            || path.Contains("LoginWith", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("Lockout", StringComparison.OrdinalIgnoreCase))
        {
            return AuthArea.SignIn;
        }

        if (path.Contains("Password", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Recovery", StringComparison.OrdinalIgnoreCase))
        {
            return AuthArea.Recover;
        }

        if (path.Contains("Register", StringComparison.OrdinalIgnoreCase)
            || path.Contains("ConfirmEmail", StringComparison.OrdinalIgnoreCase)
            || path.Contains("ResendEmail", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("ExternalLogin", StringComparison.OrdinalIgnoreCase))
        {
            return AuthArea.Register;
        }

        return null;
    }

    /// <summary>
    /// Builds a sign-in or registration link that carries forward a safe local continuation.
    /// </summary>
    /// <param name="path">The account entry route.</param>
    /// <returns>The route with the current local return URL when one is present.</returns>
    private string BuildEntryHref(string path)
    {
        var returnUrl = GetSafeReturnUrl();
        return returnUrl is null
            ? path
            : navigationManager.GetUriWithQueryParameters(
                path,
                new Dictionary<string, object?> { ["ReturnUrl"] = returnUrl });
    }

    /// <summary>
    /// Gets and validates the current return URL query value.
    /// </summary>
    /// <returns>A normalized local path, or <see langword="null"/> when the value is absent or unsafe.</returns>
    private string? GetSafeReturnUrl()
    {
        var query = QueryHelpers.ParseQuery(navigationManager.ToAbsoluteUri(navigationManager.Uri).Query);
        if (!query.TryGetValue("ReturnUrl", out var values))
        {
            return null;
        }

        var candidate = values.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(candidate)
            || !Uri.IsWellFormedUriString(candidate, UriKind.Relative)
            || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.Contains('\\'))
        {
            return null;
        }

        return candidate.StartsWith('/') ? candidate : $"/{candidate}";
    }

    /// <summary>
    /// Describes one account directory destination.
    /// </summary>
    /// <param name="Area">The area represented by the panel.</param>
    /// <param name="Label">The visible destination label.</param>
    /// <param name="Href">The destination URL.</param>
    private sealed record AuthPanel(AuthArea Area, string Label, string Href);

    /// <summary>
    /// Identifies the account areas represented in the access directory.
    /// </summary>
    private enum AuthArea
    {
        /// <summary>The local authentication area.</summary>
        SignIn,

        /// <summary>The account registration and confirmation area.</summary>
        Register,

        /// <summary>The password and recovery-code recovery area.</summary>
        Recover,

        /// <summary>The profile management area.</summary>
        Manage,
    }
}
