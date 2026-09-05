using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;

namespace Nova.Features.Players;

/// <summary>Shares exact duplicate classification between advisory previews and locked commits.</summary>
internal static class PlayerImportRowClassifier
{
    /// <summary>Classifies a bounded parsed file against tenant-visible players using one reader.</summary>
    /// <param name="db">The caller's tenant context, locked for commit or read-only for preview.</param>
    /// <param name="parsed">The structurally parsed source file.</param>
    /// <param name="cancellationToken">Cancels the database read.</param>
    /// <returns>Ordered row classifications using the existing import normalization rules.</returns>
    internal static async Task<IReadOnlyList<PlayerImportPreviewRow>> ClassifyAsync(
        ApplicationDbContext db, ParsedPlayerImport parsed, CancellationToken cancellationToken)
    {
        var readyCandidates = parsed.Rows
            .Where(row => row.Status == PlayerImportRowStatus.Ready)
            .Select(row => row.Candidate!)
            .ToList();
        var dates = readyCandidates
            .Select(candidate => candidate.DateOfBirth)
            .Distinct()
            .ToArray();

        List<ExistingPlayer> existingPlayers = dates.Length == 0
                ? []
                : await db.Players
                    .Where(player => dates.Contains(player.DateOfBirth))
                    .Select(player => new ExistingPlayer(
                        player.PlayerId,
                        player.FirstName,
                        player.LastName,
                        player.DateOfBirth,
                        player.LifecycleStatus))
                    .ToListAsync(cancellationToken);

        var existingByKey = existingPlayers
            .GroupBy(player => PlayerDuplicateKey.Create(player.FirstName, player.LastName, player.DateOfBirth))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(player => player.LifecycleStatus == LifecycleStatus.Active ? 0 : 1)
                    .ThenBy(player => player.PlayerId)
                    .First());
        var firstUploadRows = new Dictionary<PlayerDuplicateKey, int>();
        var classifiedRows = new List<PlayerImportPreviewRow>(parsed.Rows.Count);

        foreach (var row in parsed.Rows)
        {
            if (row.Status != PlayerImportRowStatus.Ready)
            {
                classifiedRows.Add(row);
                continue;
            }

            var candidate = row.Candidate!;
            var key = PlayerDuplicateKey.Create(candidate.FirstName, candidate.LastName, candidate.DateOfBirth);
            if (existingByKey.TryGetValue(key, out var existing))
            {
                classifiedRows.Add(row with
                {
                    Status = PlayerImportRowStatus.Duplicate,
                    Duplicate = new PlayerImportDuplicate(
                        existing.LifecycleStatus == LifecycleStatus.Active
                            ? PlayerImportDuplicateKind.ExistingActivePlayer
                            : PlayerImportDuplicateKind.ExistingArchivedPlayer,
                        existing.PlayerId,
                        EarlierSourceRowNumber: null)
                });
                continue;
            }

            if (firstUploadRows.TryGetValue(key, out var earlierSourceRow))
            {
                classifiedRows.Add(row with
                {
                    Status = PlayerImportRowStatus.Duplicate,
                    Duplicate = new PlayerImportDuplicate(
                        PlayerImportDuplicateKind.EarlierUploadRow,
                        ExistingPlayerId: null,
                        earlierSourceRow)
                });
                continue;
            }

            firstUploadRows.Add(key, row.SourceRowNumber);
            classifiedRows.Add(row);
        }

        return classifiedRows.AsReadOnly();
    }

    /// <summary>Projects only the facts required to classify an existing tenant player.</summary>
    /// <param name="PlayerId">The existing identity.</param>
    /// <param name="FirstName">The stored first name.</param>
    /// <param name="LastName">The stored last name.</param>
    /// <param name="DateOfBirth">The stored birth date.</param>
    /// <param name="LifecycleStatus">The active or archived classification.</param>
    private readonly record struct ExistingPlayer(
        long PlayerId,
        string FirstName,
        string LastName,
        DateOnly DateOfBirth,
        LifecycleStatus LifecycleStatus);

    /// <summary>Represents the import-specific normalized natural identity.</summary>
    /// <param name="FirstName">The normalized first name.</param>
    /// <param name="LastName">The normalized last name.</param>
    /// <param name="DateOfBirth">The unchanged birth date.</param>
    private readonly record struct PlayerDuplicateKey(string FirstName, string LastName, DateOnly DateOfBirth)
    {
        /// <summary>Applies the intake contract's trim and invariant-case normalization.</summary>
        /// <param name="firstName">The original first name.</param>
        /// <param name="lastName">The original last name.</param>
        /// <param name="dateOfBirth">The original birth date.</param>
        /// <returns>The comparison key.</returns>
        public static PlayerDuplicateKey Create(string firstName, string lastName, DateOnly dateOfBirth) => new(
            firstName.Trim().ToUpperInvariant(),
            lastName.Trim().ToUpperInvariant(),
            dateOfBirth);
    }

}
