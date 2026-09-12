using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Common;

namespace Nova.Features.Campaigns;

/// <summary>
/// Serializes an immutable Closed-campaign record to the one supported CSV shape. Campaign identity
/// is written once from the read's own snapshot, and every row keeps the campaign-local outcome and
/// attribution the read produced; nothing is recalculated from the effective season.
/// </summary>
internal static class ClosedCampaignRosterCsvWriter
{
    private static readonly UTF8Encoding _utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Builds the complete BOM-prefixed CSV file for one campaign snapshot.</summary>
    /// <param name="campaign">The campaign and season identity from the same snapshot as the rows.</param>
    /// <param name="rows">Every campaign participant, already in stable export order.</param>
    /// <returns>The UTF-8 bytes of the whole file.</returns>
    internal static byte[] Write(PlacementCampaignIdentity campaign, IReadOnlyList<ClosedCampaignRosterItem> rows)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(rows);

        using var buffer = new MemoryStream();
        buffer.Write(Encoding.UTF8.GetPreamble());

        using (var writer = new StreamWriter(buffer, _utf8WithoutBom, bufferSize: 1024, leaveOpen: true))
        using (var csv = new CsvWriter(writer, CreateConfiguration()))
        {
            foreach (var header in ClosedCampaignRosterExportConstraints.Headers)
            {
                csv.WriteField(header);
            }
            csv.NextRecord();

            foreach (var row in rows)
            {
                WriteRow(csv, campaign, row);
            }
        }

        return buffer.ToArray();
    }

    private static CsvConfiguration CreateConfiguration() => new(CultureInfo.InvariantCulture)
    {
        Delimiter = ",",
        NewLine = "\r\n",
        HasHeaderRecord = false,
        Mode = CsvMode.RFC4180,
    };

    private static void WriteRow(CsvWriter csv, PlacementCampaignIdentity campaign, ClosedCampaignRosterItem row)
    {
        csv.WriteField(CsvCellSafety.EscapeFormula(campaign.Name));
        csv.WriteField(CsvCellSafety.EscapeFormula(campaign.Season.Name));
        csv.WriteField(CsvCellSafety.EscapeFormula(row.FirstName));
        csv.WriteField(CsvCellSafety.EscapeFormula(row.LastName));
        csv.WriteField(row.TryoutNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        csv.WriteField(row.GraduationYear.ToString(CultureInfo.InvariantCulture));
        csv.WriteField(row.Source.Decision.Outcome.ToString());
        // A saved team is exported only for an Assigned outcome, so a stale team on a no-team
        // outcome can never appear as that participant's final team.
        csv.WriteField(CsvCellSafety.EscapeFormula(row.Source.Decision.Outcome == PlacementOutcome.Assigned
            ? row.Source.Team?.TeamName ?? string.Empty : string.Empty));
        csv.WriteField(CsvCellSafety.EscapeFormula(row.Source.Decision.ActorDisplayName));
        csv.WriteField(row.Source.Decision.RecordedAt.ToString("O", CultureInfo.InvariantCulture));
        csv.NextRecord();
    }
}
