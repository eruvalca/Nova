using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Shared.Account;
using Nova.Shared.Security;

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
        // Get current user ID
        if (currentUserProvider.UserId is not long userId)
        {
            throw new InvalidOperationException("User is not authenticated.");
        }

        // Re-read current facts immediately before deciding which destructive effects to apply.
        var deletionFacts = await GatherDeletionFactsAsync(cancellationToken);
        var preview = AccountDeletionPolicy.Evaluate(deletionFacts);

        // Load the current user
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new InvalidOperationException("User not found.");
        }

        // If user is the only member, delete the club
        if (preview.Scenario == AccountDeletionScenario.OnlyClubMember)
        {
            if (deletionFacts.ClubId is long clubId)
            {
                await using var adminDb = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);

                var club = await adminDb.Clubs.FindAsync([clubId], cancellationToken);
                if (club is not null)
                {
                    adminDb.Clubs.Remove(club);
                    await adminDb.SaveChangesAsync(cancellationToken);
                    LogClubDeletion(clubId, userId);
                }
            }

        }

        // Delete the user account
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            LogDeleteFailed(userId, errors);
            throw new InvalidOperationException($"Failed to delete account: {errors}");
        }
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete account for user {UserId}: {Errors}.")]
    private partial void LogDeleteFailed(long userId, string errors);
}
