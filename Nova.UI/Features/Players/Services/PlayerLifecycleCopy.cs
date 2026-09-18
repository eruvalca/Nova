using System.Globalization;
using Nova.SharedKernel.Features.Players;

namespace Nova.UI.Features.Players.Services;

/// <summary>
/// The one wording for player lifecycle actions, shared by the directory and Player detail so the
/// two hosts cannot state different consequences for the same action.
/// </summary>
internal static class PlayerLifecycleCopy
{
    /// <summary>The archive confirmation heading for one player.</summary>
    /// <param name="displayName">The player's display name.</param>
    /// <returns>The confirmation heading.</returns>
    public static string ArchiveHeading(string displayName) => $"Archive {displayName}?";

    /// <summary>Explains what archiving preserves and requires.</summary>
    public const string ArchiveConsequence =
        "Archiving preserves campaign history. Any unresolved active-campaign blockers must be resolved first.";

    /// <summary>The explicit acknowledgement the member must give before archiving.</summary>
    public const string ArchiveAcknowledge = "I understand this player will be moved to the archived roster.";

    /// <summary>The commit label for the archive confirmation.</summary>
    public const string ArchiveCommit = "Archive player";

    /// <summary>The heading above structured archive blockers.</summary>
    public const string BlockersHeading = "Archive blockers:";

    /// <summary>Explains that restoring does not recreate missed campaign participation.</summary>
    public const string RestoreNote =
        "Restoring reactivates the profile only; campaigns missed while archived are not backfilled automatically.";

    /// <summary>The result message after a successful archive.</summary>
    public const string ArchivedResult = "Player archived.";

    /// <summary>The result message after a successful restore.</summary>
    public const string RestoredResult =
        "Player restored. Missed campaign enrollment is not backfilled automatically.";

    /// <summary>Describes one blocker's campaign and participation identities.</summary>
    /// <param name="blocker">The blocker to describe.</param>
    /// <returns>The written blocker detail.</returns>
    public static string BlockerText(PlayerArchiveBlocker blocker)
    {
        ArgumentNullException.ThrowIfNull(blocker);
        var participationIds = string.Join(", ", blocker.ParticipationIds);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{blocker.CampaignName} (Campaign {blocker.CampaignId}): participation IDs {participationIds}");
    }
}
