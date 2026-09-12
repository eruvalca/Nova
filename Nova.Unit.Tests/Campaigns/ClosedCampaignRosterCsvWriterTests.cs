using System.Globalization;
using System.Text;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class ClosedCampaignRosterCsvWriterTests
{
    private const long AssignmentId = 11;
    private const long PlayerId = 22;
    private const long CampaignId = 100;
    private const long SeasonId = 20;
    private const long TeamId = 30;

    private static readonly PlacementCampaignIdentity _campaign =
        new(CampaignId, "Fall tryouts", CampaignStatus.Closed, new PlacementSeasonIdentity(SeasonId, "2026 season"));

    [Fact]
    public void WriteEmitsUtf8BomThenTheExactHeaderRow()
    {
        var bytes = ClosedCampaignRosterCsvWriter.Write(_campaign, []);

        ClosedRosterCsv.HasPreamble(bytes).ShouldBeTrue();
        ClosedRosterCsv.Cells(bytes, 0).ShouldBe(ClosedCampaignRosterExportConstraints.Headers.ToArray());
        ClosedRosterCsv.Text(bytes).ShouldEndWith("\r\n");
    }

    [Fact]
    public void WriteEmitsOneRecordPerRowInSuppliedOrder()
    {
        var bytes = ClosedCampaignRosterCsvWriter.Write(_campaign,
            [Row("Bea", "Zulu"), Row("Ada", "Alpha", outcome: PlacementOutcome.NotSelected, teamName: null)]);

        ClosedRosterCsv.Parse(bytes).Count.ShouldBe(3);
        ClosedRosterCsv.Cells(bytes, 1)[2..4].ShouldBe(["Bea", "Zulu"]);
        ClosedRosterCsv.Cells(bytes, 2)[2..4].ShouldBe(["Ada", "Alpha"]);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("=SUM(A1:A9)")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM")]
    [InlineData("\tTabbed")]
    [InlineData("\rCarriage")]
    [InlineData("\nLine")]
    [InlineData(" =Leading space")]
    public void WriteEscapesEveryFormulaLikeValueWithoutChangingTheRealName(string lastName)
    {
        var bytes = ClosedCampaignRosterCsvWriter.Write(_campaign, [Row("Ada", lastName)]);

        var cell = ClosedRosterCsv.Cells(bytes, 1)[3];
        cell.ShouldBe("'" + lastName);
        cell[1..].ShouldBe(lastName);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("Smith")]
    [InlineData("O'Neil-Smith")]
    [InlineData("Müller")]
    public void WriteLeavesOrdinaryNamesUnchanged(string lastName)
    {
        var bytes = ClosedCampaignRosterCsvWriter.Write(_campaign, [Row("Ada", lastName)]);

        ClosedRosterCsv.Cells(bytes, 1)[3].ShouldBe(lastName);
    }

    [Fact]
    public void WritePreservesLongAndNonAsciiNamesAsUtf8()
    {
        var longName = new string('A', 400) + " O'Neil-Smith";
        var bytes = ClosedCampaignRosterCsvWriter.Write(_campaign, [Row("José", "Müller"), Row("李", longName)]);

        ClosedRosterCsv.Cells(bytes, 1).ShouldBe(["Fall tryouts", "2026 season", "José", "Müller", "7", "2030",
            "Assigned", "Alpha", "Original decision maker", DecisionTime()]);
        ClosedRosterCsv.Cells(bytes, 2)[3].ShouldBe(longName);
        Encoding.UTF8.GetBytes(ClosedRosterCsv.Text(bytes)).ShouldBe(bytes[Encoding.UTF8.GetPreamble().Length..]);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("Smith, Jane", "\"Smith, Jane\"")]
    [InlineData("He said \"hi\"", "\"He said \"\"hi\"\"\"")]
    [InlineData("line1\r\nline2", "\"line1\r\nline2\"")]
    public void WriteQuotesCellsThatContainTheDelimiterQuoteOrNewline(string value, string expectedCell)
    {
        var bytes = ClosedCampaignRosterCsvWriter.Write(_campaign, [Row(value, value)]);

        ClosedRosterCsv.Text(bytes).ShouldContain(expectedCell);
        ClosedRosterCsv.Cells(bytes, 1)[3].ShouldBe(value);
    }

    [Fact]
    public void WriteOmitsTheTeamForNonAssignedOutcomesEvenWhenAStaleTeamIsSaved()
    {
        var row = Row("Ada", "Alpha", outcome: PlacementOutcome.NotSelected, teamName: "Alpha");
        row.Source.Team.ShouldNotBeNull().TeamName.ShouldBe("Alpha");

        ClosedRosterCsv.Cells(ClosedCampaignRosterCsvWriter.Write(_campaign, [row]), 1)[7].ShouldBeEmpty();
    }

    [Fact]
    public void WriteOmitsTheTeamForAnAssignedOutcomeWithoutAVisibleTeam()
    {
        var row = Row("Ada", "Alpha", teamName: null);

        ClosedRosterCsv.Cells(ClosedCampaignRosterCsvWriter.Write(_campaign, [row]), 1)[7].ShouldBeEmpty();
    }

    [Fact]
    public void WriteRendersAnUnknownTryoutNumberAsAnEmptyCell()
    {
        var bytes = ClosedCampaignRosterCsvWriter.Write(_campaign, [Row("Ada", "Alpha", tryoutNumber: null)]);

        ClosedRosterCsv.Cells(bytes, 1)[4].ShouldBeEmpty();
    }

    [Fact]
    public void WriteUsesInvariantRoundTripDecisionTimeAndGraduationYear()
    {
        var recordedAt = new DateTimeOffset(2026, 9, 11, 14, 3, 5, TimeSpan.FromHours(-5));
        var bytes = ClosedCampaignRosterCsvWriter.Write(_campaign,
            [Row("Ada", "Alpha", graduationYear: 2029, recordedAt: recordedAt)]);

        var cells = ClosedRosterCsv.Cells(bytes, 1);
        cells[5].ShouldBe("2029");
        cells[9].ShouldBe(recordedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void WriteEmitsOnlyTheExportColumnsWithoutIdentifiersOrOtherFeatureData()
    {
        var cells = ClosedRosterCsv.Cells(ClosedCampaignRosterCsvWriter.Write(_campaign, [Row("Ada", "Alpha")]), 1);

        cells.Length.ShouldBe(ClosedCampaignRosterExportConstraints.Headers.Count);
        foreach (var identifier in new[] { AssignmentId, PlayerId, CampaignId, SeasonId, TeamId })
        {
            cells.ShouldNotContain(identifier.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal);
        }
    }

    private static string DecisionTime() => DateTimeOffset.UnixEpoch.ToString("O", CultureInfo.InvariantCulture);

    private static ClosedCampaignRosterItem Row(string firstName, string lastName, int graduationYear = 2030,
        int? tryoutNumber = 7, PlacementOutcome outcome = PlacementOutcome.Assigned, string? teamName = "Alpha",
        DateTimeOffset? recordedAt = null, string actor = "Original decision maker")
    {
        var teamId = teamName is null ? (long?)null : TeamId;
        var decision = new CampaignSavedPlacementDecision(AssignmentId, PlayerId, CampaignId, SeasonId, 1,
            outcome, teamId, recordedAt ?? DateTimeOffset.UnixEpoch, 10, actor, Guid.NewGuid());
        return new ClosedCampaignRosterItem(AssignmentId, PlayerId, firstName, lastName, graduationYear,
            tryoutNumber, new PlacementDecisionSource(decision, _campaign.Name,
                teamName is null ? null : new CampaignParticipantTeamSummaryDto(TeamId, teamName)));
    }
}
