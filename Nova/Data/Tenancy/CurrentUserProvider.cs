using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Security;

namespace Nova.Data.Tenancy;

/// <summary>
/// Resolves the current user from the ambient <see cref="ClaimsPrincipal"/>, preferring the
/// HTTP context (SSR/API requests) and falling back to the Blazor
/// <see cref="AuthenticationStateProvider"/> (interactive server circuits).
/// </summary>
/// <param name="httpContextAccessor">The http Context Accessor.</param>
/// <param name="serviceProvider">The service Provider used to optionally resolve the authentication state provider.</param>
public sealed class CurrentUserProvider(IHttpContextAccessor httpContextAccessor, IServiceProvider serviceProvider) : ICurrentUserProvider
{
    /// <inheritdoc />
    public long? UserId => GetLongClaim(ClaimTypes.NameIdentifier);

    /// <inheritdoc />
    public long? ClubId => GetLongClaim(NovaClaimTypes.ClubId);

    /// <inheritdoc />
    public bool IsClubAdmin => GetPrincipal()?.IsInRole(Roles.ClubAdmin) ?? false;

    /// <inheritdoc />
    public CurrentUserState GetCurrentUserState()
    {
        // Read the principal once so all values come from a consistent snapshot.
        var principal = GetPrincipal();

        var userIdValue = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(userIdValue, out var userId))
        {
            return new Anonymous();
        }

        var clubIdValue = principal?.FindFirstValue(NovaClaimTypes.ClubId);
        return long.TryParse(clubIdValue, out var clubId)
            ? new ClubMember(userId, clubId, principal?.IsInRole(Roles.ClubAdmin) ?? false)
            : new AuthenticatedUser(userId);
    }

    private long? GetLongClaim(string claimType)
    {
        var value = GetPrincipal()?.FindFirstValue(claimType);
        return long.TryParse(value, out var parsed) ? parsed : null;
    }

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
                // Authentication state is not available outside of an interactive circuit.
                return null;
            }
        }

        return null;
    }
}
