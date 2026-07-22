using Nova.Shared.Results;
using System.Text.Json;

namespace Nova.Shared.Players;

/// <summary>
/// Describes unresolved active-campaign participation that blocks player archival.
/// </summary>
public sealed record PlayerArchiveBlocker
{
    /// <summary>
    /// Gets the active campaign identifier.
    /// </summary>
    public required long CampaignId { get; init; }

    /// <summary>
    /// Gets the active campaign display name.
    /// </summary>
    public required string CampaignName { get; init; }

    /// <summary>
    /// Gets the blocking campaign participation identifiers for the player.
    /// </summary>
    public required IReadOnlyList<long> ParticipationIds { get; init; }
}

/// <summary>
/// Helpers for writing and reading player-lifecycle conflict extensions on <see cref="ServiceProblem"/>.
/// </summary>
public static class PlayerLifecycleProblemExtensions
{
    /// <summary>
    /// The ProblemDetails extension key containing archive blockers.
    /// </summary>
    public const string ArchiveBlockersExtensionName = "archiveBlockers";

    /// <summary>
    /// Builds a ProblemDetails extension payload containing archive blockers.
    /// </summary>
    /// <param name="blockers">The archive blockers to attach to a conflict problem.</param>
    /// <returns>An extension dictionary suitable for <see cref="ServiceProblem.Conflict(string?, IReadOnlyDictionary{string, object?}?)"/>.</returns>
    public static IReadOnlyDictionary<string, object?> CreateArchiveBlockerExtensions(IReadOnlyList<PlayerArchiveBlocker> blockers)
        => new Dictionary<string, object?> { [ArchiveBlockersExtensionName] = blockers };

    extension(ServiceProblem problem)
    {
        /// <summary>
        /// Attempts to read structured archive blockers from a conflict <see cref="ServiceProblem"/>.
        /// </summary>
        /// <param name="blockers">When this method returns, contains the parsed blocker list or an empty list.</param>
        /// <returns><see langword="true"/> when blockers were present and parsed successfully; otherwise <see langword="false"/>.</returns>
        public bool TryGetArchiveBlockers(out IReadOnlyList<PlayerArchiveBlocker> blockers)
        {
            blockers = [];
            if (problem.Extensions is null
                || !problem.Extensions.TryGetValue(ArchiveBlockersExtensionName, out var raw)
                || raw is null)
            {
                return false;
            }

            if (raw is IReadOnlyList<PlayerArchiveBlocker> typedList)
            {
                blockers = typedList;
                return true;
            }

            if (raw is IEnumerable<PlayerArchiveBlocker> typedSequence)
            {
                blockers = typedSequence.ToList().AsReadOnly();
                return true;
            }

            if (raw is JsonElement element && element.ValueKind == JsonValueKind.Array)
            {
                List<PlayerArchiveBlocker>? parsed;
                try
                {
                    parsed = element.Deserialize<List<PlayerArchiveBlocker>>(
                        new JsonSerializerOptions(JsonSerializerDefaults.Web));
                }
                catch (JsonException)
                {
                    return false;
                }

                if (parsed is null)
                {
                    return false;
                }

                blockers = parsed.AsReadOnly();
                return true;
            }

            return false;
        }
    }
}
