using Microsoft.EntityFrameworkCore;
using Nova.Data.Tenancy;
using Nova.SharedKernel.Security;

namespace Nova.Data;

/// <summary>
/// The default tenant-scoped context. Query filters restrict data to the current user's club.
/// This is the migrations target.
/// </summary>
/// <param name="options">The context options.</param>
/// <param name="currentUser">The current user provider.</param>
internal class NovaDbContext(DbContextOptions<NovaDbContext> options, ICurrentUserProvider currentUser)
    : ApplicationDbContext(options, currentUser, bypassTenantFilter: false)
{
    /// <summary>
    /// Binds a fresh mutation context's filters and audit stamping to one authenticated identity before awaiting locks.
    /// A fresh retry or verification context must still match the original actor and club before binding.
    /// </summary>
    /// <param name="actorUserId">The actor captured at the operation's authorization boundary.</param>
    /// <param name="clubId">The authenticated club captured at that same boundary.</param>
    /// <returns>False when the current identity no longer owns the operation.</returns>
    internal bool TryBindCurrentUser(long actorUserId, long clubId)
    {
        var member = _currentUser.GetCurrentUserState().Match<ClubMember?>(_ => null, _ => null, user => user);
        if (member is null || member.UserId != actorUserId || member.ClubId != clubId) { return false; }
        _currentUser = new BoundCurrentUserProvider(member);
        return true;
    }

    /// <summary>Keeps EF's flat query parameters and the save interceptor on the same immutable identity.</summary>
    private sealed class BoundCurrentUserProvider(ClubMember member) : ICurrentUserProvider
    {
        public long? UserId => member.UserId;
        public long? ClubId => member.ClubId;
        public bool IsClubAdmin => member.IsClubAdmin;
        public CurrentUserState GetCurrentUserState() => member;
    }
}
