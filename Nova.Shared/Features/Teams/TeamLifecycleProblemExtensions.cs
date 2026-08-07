using System.Text.Json;
using Nova.Shared.Results;

namespace Nova.Shared.Features.Teams;

/// <summary>
/// Provides structured team lifecycle conflict payloads for service and HTTP clients.
/// </summary>
public static class TeamLifecycleProblemExtensions
{
    /// <summary>
    /// Gets the ProblemDetails extension key for archive blockers.
    /// </summary>
    public const string ArchiveBlockersExtensionName = "archiveBlockers";

    /// <summary>
    /// Gets the ProblemDetails extension key for graduation-year blockers.
    /// </summary>
    public const string GraduationYearBlockersExtensionName = "graduationYearBlockers";

    /// <summary>
    /// Creates extension values containing team archive blockers.
    /// </summary>
    /// <param name="blockers">The blockers to attach to the problem.</param>
    /// <returns>An extension dictionary suitable for a conflict problem.</returns>
    public static IReadOnlyDictionary<string, object?> CreateArchiveBlockerExtensions(
        IReadOnlyList<TeamArchiveBlocker> blockers)
        => new Dictionary<string, object?> { [ArchiveBlockersExtensionName] = blockers };

    /// <summary>
    /// Creates extension values containing graduation-year blockers.
    /// </summary>
    /// <param name="blockers">The blockers to attach to the problem.</param>
    /// <returns>An extension dictionary suitable for a conflict problem.</returns>
    public static IReadOnlyDictionary<string, object?> CreateGraduationYearBlockerExtensions(
        IReadOnlyList<TeamGraduationYearBlockerItem> blockers)
        => new Dictionary<string, object?> { [GraduationYearBlockersExtensionName] = blockers };

    extension(ServiceProblem problem)
    {
        /// <summary>
        /// Attempts to read structured team archive blockers from a conflict problem.
        /// </summary>
        /// <param name="blockers">The parsed blockers, or an empty list when absent or malformed.</param>
        /// <returns><see langword="true"/> when a valid blocker array was present.</returns>
        public bool TryGetArchiveBlockers(out IReadOnlyList<TeamArchiveBlocker> blockers)
            => TryReadBlockers(problem, ArchiveBlockersExtensionName, out blockers);

        /// <summary>
        /// Attempts to read structured graduation-year blockers from a conflict problem.
        /// </summary>
        /// <param name="blockers">The parsed blockers, or an empty list when absent or malformed.</param>
        /// <returns><see langword="true"/> when a valid blocker array was present.</returns>
        public bool TryGetGraduationYearBlockers(
            out IReadOnlyList<TeamGraduationYearBlockerItem> blockers)
            => TryReadBlockers(problem, GraduationYearBlockersExtensionName, out blockers);
    }

    private static bool TryReadBlockers<T>(
        ServiceProblem problem,
        string extensionName,
        out IReadOnlyList<T> blockers)
    {
        blockers = [];
        if (problem.Extensions is null
            || !problem.Extensions.TryGetValue(extensionName, out var raw)
            || raw is null)
        {
            return false;
        }

        if (raw is IReadOnlyList<T> typedList)
        {
            blockers = typedList;
            return true;
        }

        if (raw is IEnumerable<T> typedSequence)
        {
            blockers = typedSequence.ToList().AsReadOnly();
            return true;
        }

        if (raw is not JsonElement element || element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        try
        {
            var parsed = element.Deserialize<List<T>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (parsed is null)
            {
                return false;
            }

            blockers = parsed.AsReadOnly();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
