using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nova.Entities;

namespace Nova.Components.Account;

// This is a server-side AuthenticationStateProvider that revalidates the security stamp for the connected user
// every 30 minutes an interactive circuit is connected.
/// <summary>
/// Server-side authentication state provider that periodically revalidates the security stamp of the connected user.
/// </summary>
/// <remarks>
/// The <paramref name="loggerFactory"/> parameter is passed to the base class as required by the ASP.NET Core framework 
/// (<see cref="RevalidatingServerAuthenticationStateProvider"/>), which handles framework-level logging independently.
/// </remarks>
internal sealed class IdentityRevalidatingAuthenticationStateProvider(
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<IdentityOptions> options)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    /// <summary>
    /// Gets the interval at which authentication state revalidation occurs (30 minutes).
    /// </summary>
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    /// <summary>
    /// Validates the current authentication state by checking if the user's security stamp is still valid.
    /// </summary>
    /// <param name="authenticationState">The current authentication state to validate.</param>
    /// <param name="cancellationToken">A token to cancel the validation operation.</param>
    /// <returns>
    /// A task that resolves to <see langword="true"/> if the authentication state is valid, otherwise <see langword="false"/>.
    /// </returns>
    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        // Get the user manager from a new scope to ensure it fetches fresh data
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<NovaUserEntity>>();
        return await ValidateSecurityStampAsync(userManager, authenticationState.User);
    }

    /// <summary>
    /// Validates that a user's security stamp matches the stamp claim in the principal.
    /// </summary>
    /// <param name="userManager">The user manager service.</param>
    /// <param name="principal">The claims principal to validate.</param>
    /// <returns>
    /// A task that resolves to <see langword="true"/> if the security stamp is valid, otherwise <see langword="false"/>.
    /// </returns>
    private async Task<bool> ValidateSecurityStampAsync(UserManager<NovaUserEntity> userManager, ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return false;
        }
        else if (!userManager.SupportsUserSecurityStamp)
        {
            return true;
        }
        else
        {
            var principalStamp = principal.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
            var userStamp = await userManager.GetSecurityStampAsync(user);
            return principalStamp == userStamp;
        }
    }
}
