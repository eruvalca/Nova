using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Features.Account;
using Nova.Shared.Security;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Account;

/// <summary>
/// Server-side implementation of <see cref="IAccountDeletionService"/>: previews and executes account deletion.
/// </summary>
public sealed partial class AccountDeletionService(
    IDbContextFactory<NovaAdminDbContext> adminDbContextFactory,
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    UserManager<NovaUserEntity> userManager,
    ICurrentUserProvider currentUserProvider,
    ILogger<AccountDeletionService> logger) : IAccountDeletionService
{
    /// <inheritdoc />
    public async Task<AccountDeletionPreviewDto> GetDeletionPreviewAsync(CancellationToken cancellationToken = default)
    {
        var facts = await GatherDeletionFactsAsync(cancellationToken);
        var preview = AccountDeletionPolicy.Evaluate(facts);
        if (facts.IsAuthenticated
            && facts.UserExists
            && facts.IsClubAdmin
            && facts.ClubId.HasValue
            && currentUserProvider.UserId is long userId)
        {
            LogScenarioComputed(preview.Scenario, userId);
        }

        return preview;
    }

    /// <inheritdoc />
    public async Task DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long userId)
        {
            throw new InvalidOperationException("User is not authenticated.");
        }

        await using var probeDb = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = probeDb.Database.CreateExecutionStrategy();
        var commitAttempted = new CommitAttemptTracker();
        var outcome = await strategy.ExecuteAsync(
            (UserId: userId, CommitAttempted: commitAttempted),
            async (state, token) =>
            {
                state.CommitAttempted.Reset();
                await using var db = await adminDbContextFactory.CreateDbContextAsync(token);
                return await PersistDeletionAsync(db, state.UserId, state.CommitAttempted, token);
            },
            async (state, token) =>
            {
                if (!state.CommitAttempted.Attempted)
                {
                    return new ExecutionResult<OneOf<Success, NotFound, AccountDeletionBlocked>>(successful: false, default!);
                }

                await using var db = await adminDbContextFactory.CreateDbContextAsync(token);
                var committed = !await db.Users.AnyAsync(user => user.Id == state.UserId, token);
                return committed
                    ? new ExecutionResult<OneOf<Success, NotFound, AccountDeletionBlocked>>(successful: true, new Success())
                    : new ExecutionResult<OneOf<Success, NotFound, AccountDeletionBlocked>>(successful: false, default!);
            },
            cancellationToken);

        outcome.Switch(
            _ => { },
            _ => throw new InvalidOperationException("User not found."),
            _ => throw new InvalidOperationException("Another club administrator must be assigned before deleting this account."));
    }

    /// <summary>
    /// Deletes one user and, when they are the final member, their club in one retry-safe
    /// transaction after acquiring the shared user-then-club membership locks.
    /// </summary>
    /// <param name="db">The fresh admin context for the execution attempt.</param>
    /// <param name="userId">The user account to delete.</param>
    /// <param name="commitAttempted">Tracks whether the attempt reached its commit.</param>
    /// <param name="cancellationToken">A token that cancels database work.</param>
    /// <returns>The deletion, missing-user, or sole-administrator outcome.</returns>
    private async Task<OneOf<Success, NotFound, AccountDeletionBlocked>> PersistDeletionAsync(
        NovaAdminDbContext db,
        long userId,
        CommitAttemptTracker commitAttempted,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AcquireUserMembershipLockAsync(userId, cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return new NotFound();
        }

        var clubId = user.ClubId;
        var deleteClub = false;
        if (clubId is long currentClubId)
        {
            await db.AcquireClubMembershipLockAsync(currentClubId, cancellationToken);
            var administratorRoleId = await db.Roles
                .Where(role => role.NormalizedName == Roles.ClubAdmin.ToUpperInvariant())
                .Select(role => (long?)role.Id)
                .SingleOrDefaultAsync(cancellationToken);
            var userIsAdministrator = administratorRoleId is not null && await db.UserRoles.AnyAsync(
                role => role.UserId == userId && role.RoleId == administratorRoleId.Value,
                cancellationToken);
            var memberCount = await db.Users.CountAsync(candidate => candidate.ClubId == currentClubId, cancellationToken);
            var administratorCount = administratorRoleId is null
                ? 0
                : await (from candidate in db.Users
                         join role in db.UserRoles on candidate.Id equals role.UserId
                         where candidate.ClubId == currentClubId && role.RoleId == administratorRoleId.Value
                         select candidate.Id).CountAsync(cancellationToken);
            if (userIsAdministrator && memberCount > 1 && administratorCount <= 1)
            {
                return new AccountDeletionBlocked();
            }

            deleteClub = memberCount == 1;
            if (deleteClub)
            {
                var club = await db.Clubs.SingleOrDefaultAsync(candidate => candidate.ClubId == currentClubId, cancellationToken);
                if (club is not null)
                {
                    db.Clubs.Remove(club);
                }
            }
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        commitAttempted.MarkAttempted();
        await transaction.CommitAsync(cancellationToken);
        if (deleteClub)
        {
            LogClubDeletion(clubId!.Value, userId);
        }

        return new Success();
    }

    /// <summary>
    /// Loads the current identity, role, club, membership, and administrator facts used by deletion policy.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels database operations.</param>
    /// <returns>A fresh immutable account-deletion fact snapshot.</returns>
    private async Task<AccountDeletionFacts> GatherDeletionFactsAsync(CancellationToken cancellationToken)
    {
        if (currentUserProvider.UserId is not long userId)
        {
            return new AccountDeletionFacts(false, false, false, null, null, 0, 0);
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new AccountDeletionFacts(true, false, false, currentUserProvider.ClubId, null, 0, 0);
        }

        var isClubAdmin = await userManager.IsInRoleAsync(user, Roles.ClubAdmin);
        if (!isClubAdmin || currentUserProvider.ClubId is not long clubId)
        {
            return new AccountDeletionFacts(true, true, isClubAdmin, currentUserProvider.ClubId, null, 0, 0);
        }

        await using var readDb = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var clubFacts = await readDb.Clubs
            .Where(club => club.ClubId == clubId)
            .Select(club => new
            {
                club.Name,
                TotalMemberCount = readDb.Users.Count(candidate => candidate.ClubId == clubId)
            })
            .FirstOrDefaultAsync(cancellationToken);
        var clubAdmins = await userManager.GetUsersInRoleAsync(Roles.ClubAdmin);

        return new AccountDeletionFacts(
            true,
            true,
            true,
            clubId,
            clubFacts?.Name,
            clubFacts?.TotalMemberCount ?? 0,
            clubAdmins.Count(candidate => candidate.ClubId == clubId));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Computed account deletion scenario {Scenario} for user {UserId}.")]
    private partial void LogScenarioComputed(AccountDeletionScenario scenario, long userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting club {ClubId} as part of account deletion for user {UserId}.")]
    private partial void LogClubDeletion(long clubId, long userId);

    /// <summary>Tracks whether the current retry attempt reached transaction commit.</summary>
    private sealed class CommitAttemptTracker
    {
        private int _attempted;

        /// <summary>Gets whether the current attempt reached its commit call.</summary>
        public bool Attempted => Volatile.Read(ref _attempted) == 1;

        /// <summary>Clears the marker before a fresh execution attempt.</summary>
        public void Reset() => Volatile.Write(ref _attempted, 0);

        /// <summary>Marks the attempt immediately before committing.</summary>
        public void MarkAttempted() => Volatile.Write(ref _attempted, 1);
    }

    /// <summary>Indicates that deleting the sole administrator would orphan other members.</summary>
    private readonly record struct AccountDeletionBlocked;
}
