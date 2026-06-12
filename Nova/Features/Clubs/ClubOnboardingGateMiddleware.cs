using Nova.Shared.Security;

namespace Nova.Features.Clubs;

/// <summary>
/// Enforces the required club membership: any authenticated user who has uploaded a profile
/// photo but does not belong to a club is redirected to the club onboarding page before they
/// can use the rest of the application.
/// </summary>
/// <param name="next">The next middleware in the pipeline.</param>
public sealed class ClubOnboardingGateMiddleware(RequestDelegate next)
{
    /// <summary>
    /// The page users without a club are redirected to.
    /// </summary>
    public const string ClubOnboardingPagePath = "/Clubs/Onboarding";

    /// <summary>
    /// Path prefixes exempt from the gate: account/identity flows (including the onboarding page
    /// itself and the cookie-refresh hop), API endpoints, Blazor framework assets and the SignalR
    /// circuit endpoint, RCL static content, health checks, and error pages.
    /// </summary>
    private static readonly string[] ExemptPrefixes =
    [
        "/Account",
        "/api",
        "/_framework",
        "/_content",
        "/_blazor",
        "/health",
        "/alive",
        "/not-found",
        "/Error",
        "/favicon",
        "/Clubs"
    ];

    /// <summary>
    /// Invokes the middleware: redirects to the club onboarding page when the gate applies,
    /// otherwise forwards to the next middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the middleware execution.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        var hasPhotoClaim = context.User.HasClaim(claim => claim.Type == NovaClaimTypes.HasProfilePhoto);
        var hasClubIdClaim = context.User.HasClaim(claim => claim.Type == NovaClaimTypes.ClubId);

        if (ShouldRedirect(context.Request.Path, isAuthenticated, hasPhotoClaim, hasClubIdClaim))
        {
            var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
            context.Response.Redirect($"{ClubOnboardingPagePath}?returnUrl={returnUrl}");
            return Task.CompletedTask;
        }

        return next(context);
    }

    /// <summary>
    /// Determines whether a request should be redirected to the club onboarding page.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <param name="isAuthenticated">Whether the request has an authenticated user.</param>
    /// <param name="hasPhotoClaim">Whether the principal carries the <see cref="NovaClaimTypes.HasProfilePhoto"/> claim.</param>
    /// <param name="hasClubIdClaim">Whether the principal carries the <see cref="NovaClaimTypes.ClubId"/> claim.</param>
    /// <returns><see langword="true"/> when the gate should redirect; otherwise <see langword="false"/>.</returns>
    public static bool ShouldRedirect(PathString path, bool isAuthenticated, bool hasPhotoClaim, bool hasClubIdClaim)
    {
        if (!isAuthenticated || !hasPhotoClaim || hasClubIdClaim)
        {
            return false;
        }

        foreach (var prefix in ExemptPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Static assets (anything with a file extension) are exempt.
        return !Path.HasExtension(path.Value);
    }
}
