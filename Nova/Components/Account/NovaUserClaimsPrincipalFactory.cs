using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Nova.Entities;
using Nova.Shared.Security;

namespace Nova.Components.Account;

/// <summary>
/// Adds the <see cref="NovaClaimTypes.ClubId"/> claim (and role claims via the base factory)
/// to the user's principal at sign-in.
/// IMPORTANT: when a user's club membership changes, the cookie must be refreshed for the new
/// claim to take effect — call <c>UserManager.UpdateSecurityStampAsync(user)</c> and, for the
/// current user, <c>SignInManager.RefreshSignInAsync(user)</c>. Other users' cookies are
/// rebuilt by <see cref="IdentityRevalidatingAuthenticationStateProvider"/> on its
/// revalidation interval.
/// </summary>
/// <param name="userManager">The user Manager.</param>
/// <param name="roleManager">The role Manager.</param>
/// <param name="options">The options.</param>
public sealed class NovaUserClaimsPrincipalFactory(
    UserManager<NovaUserEntity> userManager,
    RoleManager<IdentityRole<long>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<NovaUserEntity, IdentityRole<long>>(userManager, roleManager, options)
{
    /// <inheritdoc />
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(NovaUserEntity user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (user.ClubId.HasValue)
        {
            identity.AddClaim(new Claim(NovaClaimTypes.ClubId, user.ClubId.Value.ToString()));
        }

        return identity;
    }
}
