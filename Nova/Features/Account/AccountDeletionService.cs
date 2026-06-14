using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Shared.Account;
using Nova.Shared.Results;
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
        // Get current user ID
        if (currentUserProvider.UserId is not long userId)
        {
            return new AccountDeletionPreviewDto(AccountDeletionScenario.NoClubOrNonAdmin, null, null);
        }

        // Load user and check if they are a ClubAdmin
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new AccountDeletionPreviewDto(AccountDeletionScenario.NoClubOrNonAdmin, null, null);
        }

        var isClubAdmin = await userManager.IsInRoleAsync(user, Roles.ClubAdmin);
        if (!isClubAdmin)
        {
            return new AccountDeletionPreviewDto(AccountDeletionScenario.NoClubOrNonAdmin, null, null);
        }

        // Get club ID
        if (currentUserProvider.ClubId is not long clubId)
        {
            return new AccountDeletionPreviewDto(AccountDeletionScenario.NoClubOrNonAdmin, null, null);
        }

        await using var readDb = await readDbContextFactory.CreateDbContextAsync(cancellationToken);

        // Count total members in the club
        var totalMembers = await readDb.Users.CountAsync(u => u.ClubId == clubId, cancellationToken);

        // If user is the only member, club will be deleted with the account
        if (totalMembers == 1)
        {
            var clubName = await readDb.Clubs
                .Where(c => c.ClubId == clubId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);

            LogScenarioComputed(AccountDeletionScenario.OnlyClubMember, userId);
            return new AccountDeletionPreviewDto(AccountDeletionScenario.OnlyClubMember, clubName, 0);
        }

        // Count ClubAdmins in the club
        var clubAdmins = await userManager.GetUsersInRoleAsync(Roles.ClubAdmin);
        var adminCount = clubAdmins.Count(u => u.ClubId == clubId);

        // If user is the only admin but not the only member, another admin must be assigned first
        if (adminCount == 1)
        {
            var clubName = await readDb.Clubs
                .Where(c => c.ClubId == clubId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);

            LogScenarioComputed(AccountDeletionScenario.SoleClubAdmin, userId);
            return new AccountDeletionPreviewDto(AccountDeletionScenario.SoleClubAdmin, clubName, totalMembers - 1);
        }

        // User is not the only admin, or has no club
        LogScenarioComputed(AccountDeletionScenario.NoClubOrNonAdmin, userId);
        return new AccountDeletionPreviewDto(AccountDeletionScenario.NoClubOrNonAdmin, null, null);
    }

    /// <inheritdoc />
    public async Task DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        // Get current user ID
        if (currentUserProvider.UserId is not long userId)
        {
            throw new InvalidOperationException("User is not authenticated.");
        }

        // Determine deletion scenario
        var preview = await GetDeletionPreviewAsync(cancellationToken);

        // Load the current user
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new InvalidOperationException("User not found.");
        }

        // If user is the only member, delete the club
        if (preview.Scenario == AccountDeletionScenario.OnlyClubMember)
        {
            if (currentUserProvider.ClubId is long clubId)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Computed account deletion scenario {Scenario} for user {UserId}.")]
    private partial void LogScenarioComputed(AccountDeletionScenario scenario, long userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleting club {ClubId} as part of account deletion for user {UserId}.")]
    private partial void LogClubDeletion(long clubId, long userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete account for user {UserId}: {Errors}.")]
    private partial void LogDeleteFailed(long userId, string errors);
}
