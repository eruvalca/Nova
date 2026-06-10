using Nova.Shared.Security;

namespace Nova.Features.Photos;

/// <summary>
/// Enforces the required profile photo: any authenticated user whose principal lacks the
/// <see cref="NovaClaimTypes.HasProfilePhoto"/> claim is redirected to the profile photo page
/// before they can use the rest of the application.
/// </summary>
/// <param name="next">The next middleware in the pipeline.</param>
public sealed class ProfilePhotoGateMiddleware(RequestDelegate next)
{
    /// <summary>
    /// The page users without a photo are redirected to.
    /// </summary>
    public const string ProfilePhotoPagePath = "/Account/ProfilePhoto";

    /// <summary>
    /// Path prefixes exempt from the gate: account/identity flows (including the photo page
    /// itself and the cookie-refresh hop), API endpoints (incl. the upload endpoint), Blazor
    /// framework assets and the SignalR circuit endpoint, RCL static content, health checks,
    /// and error pages.
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
        "/favicon"
    ];

    /// <summary>
    /// Invokes the middleware: redirects to the profile photo page when the gate applies,
    /// otherwise forwards to the next middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task representing the middleware execution.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        var hasPhotoClaim = context.User.HasClaim(claim => claim.Type == NovaClaimTypes.HasProfilePhoto);

        if (ShouldRedirect(context.Request.Path, isAuthenticated, hasPhotoClaim))
        {
            var returnUrl = Uri.EscapeDataString(context.Request.Path + context.Request.QueryString);
            context.Response.Redirect($"{ProfilePhotoPagePath}?returnUrl={returnUrl}");
            return Task.CompletedTask;
        }

        return next(context);
    }

    /// <summary>
    /// Determines whether a request should be redirected to the profile photo page.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <param name="isAuthenticated">Whether the request has an authenticated user.</param>
    /// <param name="hasPhotoClaim">Whether the principal carries the <see cref="NovaClaimTypes.HasProfilePhoto"/> claim.</param>
    /// <returns><see langword="true"/> when the gate should redirect; otherwise <see langword="false"/>.</returns>
    public static bool ShouldRedirect(PathString path, bool isAuthenticated, bool hasPhotoClaim)
    {
        if (!isAuthenticated || hasPhotoClaim)
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
