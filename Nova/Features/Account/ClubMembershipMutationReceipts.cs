using Microsoft.EntityFrameworkCore;
using Nova.Data;

namespace Nova.Features.Account;

/// <summary>Retention support for short-lived ambiguous-commit receipts.</summary>
internal static class ClubMembershipMutationReceipts
{
    private const int RetentionDays = 1;

    internal static async Task PruneExpiredAsync(NovaAdminDbContext db, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
        if (db.Database.IsNpgsql())
        {
            await db.ClubMembershipMutationReceipts
                .Where(receipt => receipt.CreatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var expired = (await db.ClubMembershipMutationReceipts.ToListAsync(cancellationToken))
            .Where(receipt => receipt.CreatedAt < cutoff)
            .ToList();
        if (expired.Count > 0)
        {
            db.ClubMembershipMutationReceipts.RemoveRange(expired);
        }
    }
}
