using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class ClosedCampaignRosterExportTests : IDisposable
{
    private const long ClubId = 1;
    private const long MemberId = 10;
    private const long SeasonId = 20;
    private const long ClosedCampaignId = 100;
    private const long ActiveCampaignId = 80;
    private const long DraftCampaignId = 70;
    private const long TeamId = 30;
    private const string ActorName = "Original decision maker";

    private readonly TenancyTestHarness _harness = new();

    public ClosedCampaignRosterExportTests()
    {
        Seed();
        ActAs();
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ExportContainsExactlyOneRowPerCampaignLocalParticipantAsync()
    {
        var assigned = AddPlayer("Ada", "Alpha");
        AddDecision(assigned, ClosedCampaignId, PlacementOutcome.Assigned, TeamId, tryoutNumber: 5);
        var notSelected = AddPlayer("Bea", "Beta");
        AddDecision(notSelected, ClosedCampaignId, PlacementOutcome.NotSelected, tryoutNumber: 6);

        var export = await ExportAsync(ClosedCampaignId);

        export.ContentType.ShouldBe(ClosedCampaignRosterExportConstraints.CsvContentType);
        var records = ClosedRosterCsv.Parse(export.Content);
        records.Count.ShouldBe(3);
        ClosedRosterCsv.Cells(export.Content, 1).ShouldBe(["Campaign 100", "Current season", "Ada", "Alpha", "5",
            "2028", "Assigned", "Alpha", ActorName, DecisionTime()]);
        ClosedRosterCsv.Cells(export.Content, 2).ShouldBe(["Campaign 100", "Current season", "Bea", "Beta", "6",
            "2028", "NotSelected", string.Empty, ActorName, DecisionTime()]);
    }

    [Fact]
    public async Task ExportNeverReplacesTheClosedRecordWithTheLatestEffectiveRosterAsync()
    {
        var committed = AddPlayer("Ada", "Alpha");
        AddDecision(committed, ClosedCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(committed, ActiveCampaignId, PlacementOutcome.Withdrawn);
        var activeOnly = AddPlayer("Bea", "Beta");
        AddDecision(activeOnly, ActiveCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(AddPlayer("Cara", "Gamma"), ActiveCampaignId, PlacementOutcome.NotSelected);

        var export = await ExportAsync(ClosedCampaignId);

        var records = ClosedRosterCsv.Parse(export.Content);
        records.Count.ShouldBe(2);
        ClosedRosterCsv.Cells(export.Content, 1)[6].ShouldBe(nameof(PlacementOutcome.Assigned));
        ClosedRosterCsv.Cells(export.Content, 1)[7].ShouldBe("Alpha");
        (await ExportResultAsync(ActiveCampaignId)).Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task ExportRetainsArchivedParticipantsAndArchivedTeamsAsync()
    {
        var archived = AddPlayer("Ada", "Alpha", archived: true);
        AddDecision(archived, ClosedCampaignId, PlacementOutcome.Assigned, TeamId);
        using (var db = _harness.CreateAdminContext())
        {
            var team = db.Teams.Single(row => row.TeamId == TeamId);
            team.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UnixEpoch;
            team.ArchivedById = MemberId;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var export = await ExportAsync(ClosedCampaignId);

        ClosedRosterCsv.Parse(export.Content).Count.ShouldBe(2);
        ClosedRosterCsv.Cells(export.Content, 1)[7].ShouldBe("Alpha");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.NotSelected)]
    [InlineData(PlacementOutcome.Withdrawn)]
    public async Task ExportLeavesTheFinalTeamEmptyForNoTeamOutcomesAsync(PlacementOutcome outcome)
    {
        var player = AddPlayer();
        AddDecision(player, ClosedCampaignId, outcome);

        var export = await ExportAsync(ClosedCampaignId);

        ClosedRosterCsv.Cells(export.Content, 1)[7].ShouldBeEmpty();
    }

    [Fact]
    public async Task ExportOrdersRowsByLastNameFirstNameAndPlayerIdAsync()
    {
        AddDecision(AddPlayer("Sam", "Same"), ClosedCampaignId, PlacementOutcome.NotSelected, tryoutNumber: 1);
        AddDecision(AddPlayer("Sam", "Same"), ClosedCampaignId, PlacementOutcome.NotSelected, tryoutNumber: 2);
        AddDecision(AddPlayer("Ada", "Zulu"), ClosedCampaignId, PlacementOutcome.NotSelected, tryoutNumber: 3);
        AddDecision(AddPlayer("Ada", "Alpha"), ClosedCampaignId, PlacementOutcome.NotSelected, tryoutNumber: 4);

        var export = await ExportAsync(ClosedCampaignId);

        var rows = ClosedRosterCsv.Parse(export.Content).Skip(1).ToArray();
        rows.Select(cells => (cells[3], cells[4])).ShouldBe(
            [("Alpha", "4"), ("Same", "1"), ("Same", "2"), ("Zulu", "3")]);
    }

    [Fact]
    public async Task ExportReadsTheWholeRecordInOneSnapshotWithoutTagsAsync()
    {
        var tagged = AddPlayer("Ada", "Alpha");
        var assignment = AddDecision(tagged, ClosedCampaignId, PlacementOutcome.Assigned, TeamId);
        AddApplyTag(assignment, "Pitcher");
        var interceptor = new CountingCommandInterceptor();

        var export = await ExportAsync(ClosedCampaignId, interceptor);

        // Campaign identity, the integrity probe, and the whole participant read: three readers and
        // no tag enrichment, which the paged read performs separately.
        interceptor.ReaderExecutionCount.ShouldBe(3);
        ClosedRosterCsv.Parse(export.Content).Count.ShouldBe(2);
        ClosedRosterCsv.Cells(export.Content, 1).ShouldNotContain("Pitcher", StringComparer.Ordinal);
    }

    [Fact]
    public async Task ExportEscapesFormulaLikeNamesAndPreservesRealNamesAsync()
    {
        AddDecision(AddPlayer("=cmd|calc", "Müller"), ClosedCampaignId, PlacementOutcome.NotSelected);

        var export = await ExportAsync(ClosedCampaignId);

        var cells = ClosedRosterCsv.Cells(export.Content, 1);
        cells[2].ShouldBe("'=cmd|calc");
        cells[3].ShouldBe("Müller");
    }

    [Fact]
    public async Task ExportDerivesASafeAsciiFileNameFromAnUntrustedCampaignNameAsync()
    {
        RenameCampaign(ClosedCampaignId, "U14 \"Fall\" tryouts\r\n../../etc");
        AddDecision(AddPlayer(), ClosedCampaignId, PlacementOutcome.NotSelected);

        var export = await ExportAsync(ClosedCampaignId);

        export.FileName.ShouldBe("nova-u14-fall-tryouts-etc-closed-roster.csv");
    }

    [Fact]
    public async Task ExportFallsBackToABoundedFileNameWhenTheCampaignNameHasNoSafeCharactersAsync()
    {
        RenameCampaign(ClosedCampaignId, "«»");
        AddDecision(AddPlayer(), ClosedCampaignId, PlacementOutcome.NotSelected);

        var export = await ExportAsync(ClosedCampaignId);

        export.FileName.ShouldBe("nova-campaign-closed-roster.csv");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(DraftCampaignId, ServiceProblemKind.NotFound)]
    [InlineData(9_999, ServiceProblemKind.NotFound)]
    public async Task ExportHidesNonClosedAndUnknownCampaignsAsync(long campaignId, ServiceProblemKind expected)
    {
        var result = await ExportResultAsync(campaignId);

        result.Problem.Kind.ShouldBe(expected);
    }

    [Fact]
    public async Task ExportHidesForeignCampaignsFromAnotherClubAsync()
    {
        using (var db = _harness.CreateAdminContext())
        {
            db.Clubs.Add(new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                ClubId = 2,
                Name = "Club B",
                City = "Boston",
                State = "MA",
                CreatedById = 11
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        _harness.CurrentUser.ClubId = 2;

        var result = await ExportResultAsync(ClosedCampaignId);

        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task ExportRejectsAnIncompleteDecisionRecordAsync()
    {
        AddDecision(AddPlayer("Ada", "Alpha"), ClosedCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(AddPlayer("Bea", "Beta"), ClosedCampaignId, PlacementOutcome.Undecided);

        var result = await ExportResultAsync(ClosedCampaignId);

        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, 10L)]
    [InlineData(10L, null)]
    public async Task ExportRejectsCallersWithoutApprovedMembershipAsync(long? userId, long? clubId)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;

        var result = await ExportResultAsync(ClosedCampaignId);

        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExportRejectsAnInvalidCampaignIdAsync(long campaignId)
    {
        var result = await ExportResultAsync(campaignId);

        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    [Fact]
    public async Task ExportRejectsACampaignAboveTheRowLimitAsync()
    {
        SeedParticipants(ClosedCampaignId, ClosedCampaignRosterExportConstraints.MaxRows + 1);

        var result = await ExportResultAsync(ClosedCampaignId);

        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    private static string DecisionTime() => DateTimeOffset.UnixEpoch.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    private EffectivePlacementQueryService CreateService(CountingCommandInterceptor? interceptor = null) => new(
        new TestDbContextFactory<Nova.Data.NovaReadDbContext>(() => interceptor is null
            ? _harness.CreateReadContext() : _harness.CreateReadContext(interceptor)),
        _harness.CurrentUser, NullLogger<EffectivePlacementQueryService>.Instance);

    private Task<ServiceResult<ClosedCampaignRosterExport>> ExportResultAsync(long campaignId,
        CountingCommandInterceptor? interceptor = null)
        => CreateService(interceptor).ExportClosedCampaignRosterAsync(
            new() { CampaignId = campaignId }, TestContext.Current.CancellationToken);

    private async Task<ClosedCampaignRosterExport> ExportAsync(long campaignId,
        CountingCommandInterceptor? interceptor = null)
    {
        var result = await ExportResultAsync(campaignId, interceptor);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private void ActAs()
    {
        _harness.CurrentUser.UserId = MemberId;
        _harness.CurrentUser.ClubId = ClubId;
        _harness.CurrentUser.IsClubAdmin = false;
    }

    private PlayerEntity AddPlayer(string firstName = "Ada", string lastName = "Alpha", bool archived = false)
    {
        using var db = _harness.CreateAdminContext();
        var player = Player(firstName, lastName, archived);
        db.Players.Add(player);
        db.SaveChanges();
        return player;
    }

    private static PlayerEntity Player(string firstName, string lastName, bool archived = false) => new()
    {
        CreationOperationId = Guid.NewGuid(),
        ClubId = ClubId,
        CreatedById = MemberId,
        FirstName = firstName,
        LastName = lastName,
        GraduationYear = 2028,
        DateOfBirth = new DateOnly(2010, 1, 1),
        LifecycleStatus = archived ? LifecycleStatus.Archived : LifecycleStatus.Active,
        ArchivedAt = archived ? DateTimeOffset.UnixEpoch : null,
        ArchivedById = archived ? MemberId : null
    };

    private PlayerCampaignAssignmentEntity AddDecision(PlayerEntity player, long campaignId, PlacementOutcome outcome,
        long? teamId = null, int? tryoutNumber = null)
    {
        using var db = _harness.CreateAdminContext();
        var row = Assignment(player.PlayerId, campaignId, outcome, teamId, tryoutNumber);
        db.PlayerCampaignAssignments.Add(row);
        db.SaveChanges();
        return row;
    }

    private static PlayerCampaignAssignmentEntity Assignment(long playerId, long campaignId, PlacementOutcome outcome,
        long? teamId, int? tryoutNumber)
    {
        var saved = outcome != PlacementOutcome.Undecided;
        return new PlayerCampaignAssignmentEntity
        {
            PlayerId = playerId,
            CampaignId = campaignId,
            ClubId = ClubId,
            CreatedById = MemberId,
            PlacementOutcome = outcome,
            TeamId = teamId,
            TryoutNumber = tryoutNumber,
            ConcurrencyToken = Guid.NewGuid(),
            DecisionRecordedAt = saved ? DateTimeOffset.UnixEpoch : null,
            DecisionRecordedById = saved ? MemberId : null,
            DecisionActorDisplayName = saved ? ActorName : null
        };
    }

    private void AddApplyTag(PlayerCampaignAssignmentEntity assignment, string name)
    {
        using var db = _harness.CreateAdminContext();
        var tag = new PlayerTagEntity
        {
            CreationOperationId = Guid.NewGuid(),
            ClubId = ClubId,
            CreatedById = MemberId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Color = "#123456"
        };
        db.PlayerTags.Add(tag);
        db.SaveChanges();
        db.CampaignTagApplications.Add(new CampaignTagApplicationEntity
        {
            AuthorDisplayName = "Seeded evaluator",
            CreationOperationId = Guid.NewGuid(),
            ClubId = ClubId,
            CreatedById = MemberId,
            PlayerTagId = tag.PlayerTagId,
            PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId
        });
        db.SaveChanges();
    }

    private void RenameCampaign(long campaignId, string name)
    {
        using var db = _harness.CreateAdminContext();
        db.Campaigns.Single(row => row.CampaignId == campaignId).Name = name;
        db.SaveChanges();
    }

    private void SeedParticipants(long campaignId, int count)
    {
        using var db = _harness.CreateAdminContext();
        var players = Enumerable.Range(0, count).Select(index => Player($"P{index}", "Bulk")).ToList();
        db.Players.AddRange(players);
        db.SaveChanges();
        db.PlayerCampaignAssignments.AddRange(players.Select((player, index) =>
            Assignment(player.PlayerId, campaignId, PlacementOutcome.NotSelected, null, index + 1)));
        db.SaveChanges();
    }

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.Add(new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            ClubId = ClubId,
            Name = "Club A",
            City = "Austin",
            State = "TX",
            CreatedById = MemberId
        });
        db.Users.Add(new NovaUserEntity { Id = MemberId, FirstName = "Member", LastName = "A", ClubId = ClubId });
        db.Seasons.Add(new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            SeasonId = SeasonId,
            ClubId = ClubId,
            Name = "Current season",
            StartDate = new DateOnly(2026, 1, 1),
            CreatedById = MemberId
        });
        db.Teams.Add(new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            TeamId = TeamId,
            ClubId = ClubId,
            Name = "Alpha",
            GraduationYear = 2028,
            CreatedById = MemberId
        });
        db.SaveChanges();
        db.Clubs.Single(row => row.ClubId == ClubId).CurrentSeasonId = SeasonId;
        db.Campaigns.AddRange(
            Campaign(ClosedCampaignId, CampaignStatus.Closed, 1),
            Campaign(ActiveCampaignId, CampaignStatus.Active, 3),
            Campaign(DraftCampaignId, CampaignStatus.Draft, null));
        db.SaveChanges();
    }

    private static CampaignEntity Campaign(long id, CampaignStatus status, long? sequence) => new()
    {
        CreationOperationId = Guid.NewGuid(),
        CampaignId = id,
        ClubId = ClubId,
        SeasonId = SeasonId,
        CreatedById = MemberId,
        Name = "Campaign " + id,
        StartDate = new DateOnly(2026, 6, 1),
        Status = status,
        SeasonOpeningSequence = sequence,
        ClosedAt = status == CampaignStatus.Closed ? DateTimeOffset.UnixEpoch : null,
        ClosedById = status == CampaignStatus.Closed ? MemberId : null
    };
}
