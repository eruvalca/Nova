using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nova.Data;
using Nova.Entities;
using Nova.Shared.Security;

namespace Nova.Components.Account;

/// <summary>
/// Adds the <see cref="NovaClaimTypes.ClubId"/>, <see cref="NovaClaimTypes.ClubName"/>,
/// and <see cref="NovaClaimTypes.HasProfilePhoto"/> claims (and role claims via the base factory)
/// to the user's principal at sign-in.
/// IMPORTANT: when a user's club membership changes, rotate the security stamp through the owning
/// mutation. For the current user, reissue the cookie with
/// <c>SignInManager.RefreshSignInAsync(user)</c>; other users' cookies are rebuilt by
/// <see cref="IdentityRevalidatingAuthenticationStateProvider"/> on its revalidation interval.
/// The same refresh is required when a club's display name changes.
/// </summary>
/// <param name="userManager">The user Manager.</param>
/// <param name="roleManager">The role Manager.</param>
/// <param name="options">The options.</param>
/// <param name="adminDbContextFactory">The factory for the unfiltered admin context, used to check photo existence during sign-in (the request principal is not yet the user being signed in, so tenant-filtered contexts cannot be used here).</param>
public sealed class NovaUserClaimsPrincipalFactory(
    UserManager<NovaUserEntity> userManager,
    RoleManager<IdentityRole<long>> roleManager,
    IOptions<IdentityOptions> options,
    IDbContextFactory<NovaAdminDbContext> adminDbContextFactory)
    : UserClaimsPrincipalFactory<NovaUserEntity, IdentityRole<long>>(userManager, roleManager, options)
{
    /// <inheritdoc />
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(NovaUserEntity user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        await using var dbContext = await adminDbContextFactory.CreateDbContextAsync();

        if (user.ClubId.HasValue)
        {
            identity.AddClaim(new Claim(NovaClaimTypes.ClubId, user.ClubId.Value.ToString()));

            var clubName = await dbContext.Clubs
                .Where(c => c.ClubId == user.ClubId.Value)
                .Select(c => c.Name)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrEmpty(clubName))
            {
                identity.AddClaim(new Claim(NovaClaimTypes.ClubName, clubName));
            }

            var hasCrest = await dbContext.ClubCrests.AnyAsync(c => c.ClubId == user.ClubId.Value);
            if (hasCrest)
            {
                identity.AddClaim(new Claim(NovaClaimTypes.HasClubCrest, "true"));
            }
        }

        var hasPhoto = await dbContext.NovaUserPhotos.AnyAsync(p => p.NovaUserId == user.Id);
        if (hasPhoto)
        {
            identity.AddClaim(new Claim(NovaClaimTypes.HasProfilePhoto, "true"));
        }

        return identity;
    }
}
