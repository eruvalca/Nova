using Microsoft.EntityFrameworkCore;
using Nova.Data;

namespace Nova.Features.Tags;

/// <summary>
/// Shared retention and pruning for tag-definition mutation receipts. Both the management and
/// lifecycle services use this so a retention or verification change applies consistently.
/// </summary>
internal static class TagDefinitionMutationReceipts
{
    /// <summary>
    /// The number of days a tag-definition mutation receipt is retained before it is pruned.
    /// </summary>
    internal const int RetentionDays = 1;

    /// <summary>
    /// Removes tag-definition mutation receipts older than the retention window. Receipts exist only
    /// to resolve ambiguous-commit verification, so keeping them beyond the retention window is
    /// unnecessary storage growth.
    /// </summary>
    /// <param name="db">The tenant context for the current execution attempt.</param>
    /// <param name="cancellationToken">A token that cancels the delete operation.</param>
    internal static async Task PruneExpiredAsync(NovaDbContext db, CancellationToken cancellationToken)
    {
        var retentionCutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
        if (db.Database.IsNpgsql())
        {
            await db.TagDefinitionMutationReceipts
                .Where(receipt => receipt.CreatedAt < retentionCutoff)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        // SQLite cannot translate DateTimeOffset comparisons to SQL, so the candidate set is loaded
        // and the age filter is applied in memory.
        var expiredReceipts = (await db.TagDefinitionMutationReceipts
                .ToListAsync(cancellationToken))
            .Where(receipt => receipt.CreatedAt < retentionCutoff)
            .ToList();
        if (expiredReceipts.Count > 0)
        {
            db.TagDefinitionMutationReceipts.RemoveRange(expiredReceipts);
        }
    }
}
