#pragma warning disable CA1819 // Binary transfer contracts expose buffers for JSON base64, multipart, and file responses.
using System.Text;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>
/// The stable limits, wire values, and column contract of the immutable Closed-campaign roster CSV
/// export. Shared by the producer, the WASM client's download validation, and tests.
/// </summary>
public static class ClosedCampaignRosterExportConstraints
{
    /// <summary>
    /// The maximum number of participant rows one export may contain. A campaign above this bound is
    /// rejected rather than truncated, so the file never silently omits participants.
    /// </summary>
    public const int MaxRows = 10_000;

    /// <summary>The CSV response media type, including the mandatory UTF-8 charset.</summary>
    public const string CsvContentType = "text/csv; charset=utf-8";

    /// <summary>The mandatory prefix of the sanitized download filename.</summary>
    public const string FileNamePrefix = "nova-";

    /// <summary>The mandatory suffix of the sanitized download filename.</summary>
    public const string FileNameSuffix = "-closed-roster.csv";

    /// <summary>The base name used when a campaign name yields no filename-safe characters.</summary>
    public const string FallbackFileBaseName = "campaign";

    /// <summary>The longest filename base name produced from a campaign name.</summary>
    public const int MaxFileBaseNameCharacters = 60;

    /// <summary>The exact ordered header row of the export.</summary>
    public static IReadOnlyList<string> Headers { get; } =
    [
        "Campaign",
        "Season",
        "First name",
        "Last name",
        "Tryout number",
        "Graduation year",
        "Final outcome",
        "Final team",
        "Decision author",
        "Decision time"
    ];

    /// <summary>
    /// Builds the safe download filename for a campaign. Only lowercase ASCII letters, digits, and
    /// single dashes survive, so an untrusted campaign name can never inject a header, quote, path
    /// separator, or non-ASCII byte into the content disposition.
    /// </summary>
    /// <param name="campaignName">The untrusted campaign display name.</param>
    /// <returns>A bounded filename ending in <see cref="FileNameSuffix"/>.</returns>
    public static string CreateFileName(string campaignName)
        => FileNamePrefix + CreateBaseName(campaignName) + FileNameSuffix;

    private static string CreateBaseName(string campaignName)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var character in campaignName ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                current.Append(char.ToLowerInvariant(character));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }
        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        var baseName = string.Join('-', tokens);
        if (baseName.Length > MaxFileBaseNameCharacters)
        {
            baseName = baseName[..MaxFileBaseNameCharacters].TrimEnd('-');
        }
        return baseName.Length == 0 ? FallbackFileBaseName : baseName;
    }
}

/// <summary>Represents one generated immutable Closed-campaign roster export.</summary>
/// <param name="Content">The exact UTF-8 BOM-prefixed CSV bytes.</param>
/// <param name="ContentType">The response content type.</param>
/// <param name="FileName">The sanitized, campaign-derived download filename.</param>
public sealed record ClosedCampaignRosterExport(byte[] Content, string ContentType, string FileName);

#pragma warning restore CA1819
