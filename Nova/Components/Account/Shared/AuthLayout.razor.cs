using Microsoft.AspNetCore.Components;

namespace Nova.Components.Account.Shared;

/// <summary>
/// Provides the shared static-SSR shell and route-aware directory for account access pages.
/// </summary>
public partial class AuthLayout(NavigationManager navigationManager)
{
    private static readonly AuthPanel[] Panels =
    [
        new(AuthArea.SignIn, "Sign in", "Account/Login"),
        new(AuthArea.Register, "Register", "Account/Register"),
        new(AuthArea.Recover, "Recover access", "Account/ForgotPassword"),
        new(AuthArea.Manage, "Manage profile", "Account/Manage"),
    ];

    private AuthArea CurrentArea => ResolveArea(navigationManager.ToBaseRelativePath(navigationManager.Uri));

    private string GetPanelClass(AuthArea area) => IsActive(area) ? "nav-link active" : "nav-link";

    private bool IsActive(AuthArea area) => CurrentArea == area;

    private static AuthArea ResolveArea(string relativePath)
    {
        var path = relativePath.Split('?', '#')[0].TrimEnd('/');

        if (path.StartsWith("Account/Manage", StringComparison.OrdinalIgnoreCase))
        {
            return AuthArea.Manage;
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

        return AuthArea.SignIn;
    }

    private sealed record AuthPanel(AuthArea Area, string Label, string Href);

    private enum AuthArea
    {
        SignIn,
        Register,
        Recover,
        Manage,
    }
}
