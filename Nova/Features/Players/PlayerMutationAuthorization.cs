using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Features.Common;

namespace Nova.Features.Players;

/// <summary>Serializes manual player commands and result disclosure with persisted membership changes.</summary>
internal static class PlayerMutationAuthorization
{
    /// <summary>Requires the original actor to remain a member while holding membership locks until transaction disposal.</summary>
    internal static async Task<bool> AuthorizeAsync(NovaDbContext db, long actor, long club, CancellationToken token)
    {
        await db.AcquireUserMembershipLockAsync(actor, token);
        await db.AcquireClubMembershipLockAsync(club, token);
        return await db.Users.AnyAsync(user => user.Id == actor && user.ClubId == club, token);
    }
}
