using System.Globalization;
using Nova.SharedKernel.Features.Players;

namespace Nova.UI.Features.Players.Services;

/// <summary>
/// Reads the structured graduation-year blockers a server conflict reports, so every surface that
/// commits a profile correction renders the same campaign and team requirements.
/// </summary>
internal static class PlayerGraduationYearBlockers
{
    /// <summary>
    /// Extracts structured graduation-year blockers from a conflict error payload.
    /// </summary>
    /// <param name="errors">The service-problem errors dictionary.</param>
    /// <returns>A parsed list of blocker items, or an empty list when unavailable.</returns>
#pragma warning disable CA1859 // The helper returns both an empty array and a read-only list; the interface describes both results.
    public static IReadOnlyList<GraduationYearBlockerItem> Extract(
#pragma warning restore CA1859
        IReadOnlyDictionary<string, string[]>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return [];
        }

        var blockers = new Dictionary<int, GraduationYearBlockerBuilder>();
        foreach (var (key, values) in errors)
        {
            if (values.Length == 0 || !TryParseBlockerKey(key, out var index, out var fieldName))
            {
                continue;
            }

            if (!blockers.TryGetValue(index, out var builder))
            {
                builder = new GraduationYearBlockerBuilder();
                blockers[index] = builder;
            }

            var value = values[0];
            switch (fieldName)
            {
                case "assignmentId":
                    builder.PlayerCampaignAssignmentId = TryParseLong(value);
                    break;
                case "campaignId":
                    builder.CampaignId = TryParseLong(value);
                    break;
                case "teamId":
                    builder.TeamId = TryParseLong(value);
                    break;
                case "teamGraduationYear":
                    builder.TeamGraduationYear = TryParseInt(value);
                    break;
            }
        }

        return blockers
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .Where(builder =>
                builder.PlayerCampaignAssignmentId is not null
                && builder.CampaignId is not null
                && builder.TeamId is not null
                && builder.TeamGraduationYear is not null)
            .Select(builder => new GraduationYearBlockerItem
            {
                PlayerCampaignAssignmentId = builder.PlayerCampaignAssignmentId!.Value,
                CampaignId = builder.CampaignId!.Value,
                TeamId = builder.TeamId!.Value,
                TeamGraduationYear = builder.TeamGraduationYear!.Value
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Parses one blocker payload key in the format <c>blockers[{index}].{field}</c>.
    /// </summary>
    /// <param name="key">The input key.</param>
    /// <param name="index">The parsed blocker index.</param>
    /// <param name="fieldName">The parsed field name.</param>
    /// <returns><see langword="true"/> when parsing succeeds; otherwise <see langword="false"/>.</returns>
    private static bool TryParseBlockerKey(string key, out int index, out string fieldName)
    {
        index = default;
        fieldName = string.Empty;

        if (!key.StartsWith("blockers[", StringComparison.Ordinal))
        {
            return false;
        }

        var closeBracketIndex = key.IndexOf(']', StringComparison.Ordinal);
        var dotIndex = key.IndexOf('.', closeBracketIndex + 1);
        if (closeBracketIndex <= "blockers[".Length || dotIndex < 0)
        {
            return false;
        }

        var indexText = key["blockers[".Length..closeBracketIndex];
        if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            return false;
        }

        fieldName = key[(dotIndex + 1)..];
        return fieldName.Length > 0;
    }

    /// <summary>Parses a long using invariant culture.</summary>
    /// <param name="value">The incoming number text.</param>
    /// <returns>The parsed long value, or <see langword="null"/> when parsing fails.</returns>
    private static long? TryParseLong(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>Parses an int using invariant culture.</summary>
    /// <param name="value">The incoming number text.</param>
    /// <returns>The parsed int value, or <see langword="null"/> when parsing fails.</returns>
    private static int? TryParseInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>Stores one partially parsed graduation-year blocker row.</summary>
    private sealed class GraduationYearBlockerBuilder
    {
        /// <summary>Gets or sets the participation identifier.</summary>
        public long? PlayerCampaignAssignmentId { get; set; }

        /// <summary>Gets or sets the campaign identifier.</summary>
        public long? CampaignId { get; set; }

        /// <summary>Gets or sets the team identifier.</summary>
        public long? TeamId { get; set; }

        /// <summary>Gets or sets the team graduation-year requirement.</summary>
        public int? TeamGraduationYear { get; set; }
    }
}
