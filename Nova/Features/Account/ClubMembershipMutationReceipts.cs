using Microsoft.EntityFrameworkCore;
using Nova.Data;

namespace Nova.Features.Account;

/// <summary>Retention support for short-lived ambiguous-commit receipts.</summary>
internal static class ClubMembershipMutationReceipts
{
    /// <summary>The number of days ambiguous-commit receipts remain available for verification.</summary>
    private const int RetentionDays = 1;

    /// <summary>Removes membership mutation receipts older than the verification window.</summary>
    /// <param name="db">The admin context participating in the current mutation transaction.</param>
    /// <param name="clubId">The club whose expired receipts may be removed.</param>
    /// <param name="cancellationToken">A token that cancels receipt cleanup.</param>
    /// <returns>A task representing receipt cleanup.</returns>
    internal static async Task PruneExpiredAsync(
        NovaAdminDbContext db,
        long clubId,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
        if (db.Database.IsNpgsql())
        {
            await db.ClubMembershipMutationReceipts
                .Where(receipt => receipt.ClubId == clubId && receipt.CreatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var expired = (await db.ClubMembershipMutationReceipts
                .Where(receipt => receipt.ClubId == clubId)
                .ToListAsync(cancellationToken))
            .Where(receipt => receipt.CreatedAt < cutoff)
            .ToList();
        if (expired.Count > 0)
        {
            db.ClubMembershipMutationReceipts.RemoveRange(expired);
        }
    }
}
