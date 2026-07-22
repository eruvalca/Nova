using Nova.Shared.Account;

namespace Nova.Features.Account;

/// <summary>
/// Captures the current identity and club facts required to classify account deletion.
/// </summary>
/// <param name="IsAuthenticated">Whether a current user identifier is available.</param>
/// <param name="UserExists">Whether the current Identity user exists.</param>
/// <param name="IsClubAdmin">Whether the current user holds the club-administrator role.</param>
/// <param name="ClubId">The current user's club identifier when present.</param>
/// <param name="ClubName">The current club name when found.</param>
/// <param name="TotalMemberCount">The current number of club members.</param>
/// <param name="ClubAdminCount">The current number of club administrators.</param>
internal sealed record AccountDeletionFacts(
    bool IsAuthenticated,
    bool UserExists,
    bool IsClubAdmin,
    long? ClubId,
    string? ClubName,
    int TotalMemberCount,
    int ClubAdminCount);

/// <summary>
/// Classifies account-deletion implications from explicit current identity and club facts.
/// </summary>
internal static class AccountDeletionPolicy
{
    /// <summary>
    /// Creates the account-deletion preview represented by the supplied facts.
    /// </summary>
    /// <param name="facts">The current identity, role, club, membership, and administrator facts.</param>
    /// <returns>The applicable account-deletion scenario and display values.</returns>
    internal static AccountDeletionPreviewDto Evaluate(AccountDeletionFacts facts)
    {
        if (!facts.IsAuthenticated
            || !facts.UserExists
            || !facts.IsClubAdmin
            || !facts.ClubId.HasValue)
        {
            return new AccountDeletionPreviewDto(AccountDeletionScenario.NoClubOrNonAdmin, null, null);
        }

        if (facts.TotalMemberCount == 1)
        {
            return new AccountDeletionPreviewDto(
                AccountDeletionScenario.OnlyClubMember,
                facts.ClubName,
                0);
        }

        return facts.ClubAdminCount == 1
            ? new AccountDeletionPreviewDto(
                AccountDeletionScenario.SoleClubAdmin,
                facts.ClubName,
                facts.TotalMemberCount - 1)
            : new AccountDeletionPreviewDto(AccountDeletionScenario.NoClubOrNonAdmin, null, null);
    }
}
