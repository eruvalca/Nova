using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using OneOf;
using OneOf.Types;

namespace Nova.Components.Account;

/// <summary>
/// Helper for refreshing a user's authentication cookie after their club membership changes,
/// so the <see cref="Nova.Shared.Security.NovaClaimTypes.ClubId"/> claim (and roles) are rebuilt.
/// </summary>
/// <param name="userManager">The user Manager.</param>
/// <param name="signInManager">The sign In Manager.</param>
public sealed class ClubMembershipClaimRefresher(UserManager<NovaUserEntity> userManager, SignInManager<NovaUserEntity> signInManager)
{
    /// <summary>Reissues the current user's cookie after its stamp was changed transactionally.</summary>
    public async Task<OneOf<Success, Error<string[]>>> RefreshCurrentUserSignInAsync(NovaUserEntity user)
    {
        await signInManager.RefreshSignInAsync(user);
        return new Success();
    }

    /// <summary>
    /// Refreshes claims after the CURRENT user's club membership changed (e.g. they created a
    /// club or left one). Bumps the security stamp and reissues the sign-in cookie.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns>
    /// <see cref="Success"/> when the stamp was updated and the cookie reissued;
    /// <see cref="Error{T}"/> with the Identity error descriptions when the stamp update failed
    /// (the cookie is left untouched).
    /// </returns>
    public async Task<OneOf<Success, Error<string[]>>> RefreshCurrentUserAsync(NovaUserEntity user)
    {
        var result = await userManager.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            return new Error<string[]>([.. result.Errors.Select(e => e.Description)]);
        }

        await signInManager.RefreshSignInAsync(user);
        return new Success();
    }

    /// <summary>
    /// Marks ANOTHER user's claims as stale after their membership changed (e.g. an admin
    /// approved their join request). Their cookie cannot be reissued from here; bumping the
    /// security stamp causes <see cref="IdentityRevalidatingAuthenticationStateProvider"/>
    /// to rebuild their principal at the next revalidation interval.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns>
    /// <see cref="Success"/> when the security stamp was updated;
    /// <see cref="Error{T}"/> with the Identity error descriptions otherwise.
    /// </returns>
    public async Task<OneOf<Success, Error<string[]>>> MarkUserClaimsStaleAsync(NovaUserEntity user)
    {
        var result = await userManager.UpdateSecurityStampAsync(user);
        return result.Succeeded
            ? new Success()
            : new Error<string[]>([.. result.Errors.Select(e => e.Description)]);
    }

    /// <summary>
    /// Marks ALL members of the given club as stale after a club-wide change that affects
    /// every member's claims (e.g. the club crest was changed/removed). Bumping each member's
    /// security stamp causes <see cref="IdentityRevalidatingAuthenticationStateProvider"/>
    /// to rebuild their principal at the next revalidation interval.
    /// </summary>
    /// <param name="clubId">The club whose members should be marked stale.</param>
    /// <returns>
    /// <see cref="Success"/> when every member's security stamp was updated;
    /// <see cref="Error{T}"/> with the Identity error descriptions otherwise.
    /// </returns>
    public async Task<OneOf<Success, Error<string[]>>> MarkClubUsersClaimsStaleAsync(long clubId)
    {
        var clubMembers = await userManager.Users
            .Where(u => u.ClubId == clubId)
            .ToListAsync();

        foreach (var member in clubMembers)
        {
            var result = await userManager.UpdateSecurityStampAsync(member);
            if (!result.Succeeded)
            {
                return new Error<string[]>([.. result.Errors.Select(e => e.Description)]);
            }
        }

        return new Success();
    }
}
