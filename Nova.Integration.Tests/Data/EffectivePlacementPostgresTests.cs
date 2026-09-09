using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>Exercises bounded effective-placement queries against PostgreSQL and real tenant filters.</summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class EffectivePlacementPostgresTests(NovaAppHostFixture fixture)
{
    [Fact]
    public async Task LatestOpeningSequenceWinsOverCampaignIdTimestampAndTechnicalEnrollmentAsync()
    {
        var seed = await SeedAsync(3);
        using var user = fixture.UseUser(1, seed.ClubId, isClubAdmin: false);
        var service = CreateService();

        var result = (await service.GetCurrentSeasonRosterAsync(new(), TestContext.Current.CancellationToken))
            .Value.ShouldBeOfType<CurrentSeasonRosterResult>();

        result.Season!.SeasonId.ShouldBe(seed.SeasonId);
        result.Roster.TotalCount.ShouldBe(3);
        result.Roster.Items.Select(row => row.PlayerId).ShouldBe(seed.PlayerIds);
        foreach (var row in result.Roster.Items)
        {
            row.Source.Decision.CampaignId.ShouldBe(seed.LatestClosedId);
            row.Source.Decision.SeasonOpeningSequence.ShouldBe(2);
            row.Source.Team!.TeamId.ShouldBe(seed.LatestTeamId);
            row.Source.Decision.RecordedAt.ShouldBe(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        }
        var oldTeam = (await service.GetCurrentSeasonRosterAsync(new() { TeamId = seed.OldTeamId }, TestContext.Current.CancellationToken))
            .Value.ShouldBeOfType<CurrentSeasonRosterResult>();
        oldTeam.Roster.TotalCount.ShouldBe(0);
        oldTeam.Roster.Items.ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.NotSelected, EffectivePlacementEligibility.NeedsPlacement)]
    [InlineData(PlacementOutcome.Withdrawn, EffectivePlacementEligibility.Unavailable)]
    public async Task LatestTeamlessDecisionSuppressesOlderAssignmentAsync(PlacementOutcome outcome, EffectivePlacementEligibility eligibility)
    {
        var seed = await SeedAsync(1);
        await using var db = fixture.CreateAdminContext();
        var decision = await db.PlayerCampaignAssignments.SingleAsync(a => a.CampaignId == seed.LatestClosedId, TestContext.Current.CancellationToken);
        decision.PlacementOutcome = outcome;
        decision.TeamId = null;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var user = fixture.UseUser(1, seed.ClubId, isClubAdmin: true);
        var service = CreateService();

        var roster = (await service.GetCurrentSeasonRosterAsync(new(), TestContext.Current.CancellationToken)).Value.ShouldBeOfType<CurrentSeasonRosterResult>();
        roster.Roster.TotalCount.ShouldBe(0);
        var working = (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = seed.ActiveId }, TestContext.Current.CancellationToken))
            .Value.ShouldBeOfType<CampaignEffectivePlacementsResult>();
        var row = working.Participants.Items.ShouldHaveSingleItem();
        row.LocalDecision.ShouldBeNull();
        row.EffectiveDecision!.Decision.Outcome.ShouldBe(outcome);
        row.EffectiveTeam.ShouldBeNull();
        row.Eligibility.ShouldBe(eligibility);
        working.Counts.NeedsPlacement.ShouldBe(outcome == PlacementOutcome.NotSelected ? 1 : 0);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(true, PlacementCorrectionReason.TeamArchived)]
    [InlineData(false, PlacementCorrectionReason.TeamIncompatible)]
    public async Task InvalidLatestTeamEntersNeedsPlacementWithoutFallingBackAsync(bool archived, PlacementCorrectionReason reason)
    {
        var seed = await SeedAsync(1);
        await using var db = fixture.CreateAdminContext();
        var team = await db.Teams.SingleAsync(t => t.TeamId == seed.LatestTeamId, TestContext.Current.CancellationToken);
        if (archived)
        {
            team.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UtcNow;
            team.ArchivedById = 1;
        }
        else
        {
            team.GraduationYear = 2040;
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var user = fixture.UseUser(1, seed.ClubId, isClubAdmin: false);
        var service = CreateService();
        var roster = (await service.GetCurrentSeasonRosterAsync(new(), TestContext.Current.CancellationToken)).Value.ShouldBeOfType<CurrentSeasonRosterResult>();
        roster.Roster.TotalCount.ShouldBe(0);
        var working = (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = seed.ActiveId }, TestContext.Current.CancellationToken))
            .Value.ShouldBeOfType<CampaignEffectivePlacementsResult>();
        working.Counts.ShouldBe(new EffectivePlacementCounts(1, 0, 0, 0));
        var row = working.Participants.Items.ShouldHaveSingleItem();
        row.EffectiveDecision!.Decision.TeamId.ShouldBe(seed.LatestTeamId);
        row.EffectiveDecision.Decision.Outcome.ShouldBe(PlacementOutcome.Assigned);
        row.EffectiveTeam.ShouldBeNull();
        row.Eligibility.ShouldBe(EffectivePlacementEligibility.NeedsPlacement);
        row.CorrectionReason.ShouldBe(reason);
    }

    [Fact]
    public async Task DuplicateNamesPageByPlayerIdWithUnfilteredWorkingTotalsAndConstantReaderCountAsync()
    {
        var seed = await SeedAsync(12);
        using var user = fixture.UseUser(1, seed.ClubId, isClubAdmin: false);
        var counter = new CountingCommandInterceptor();
        var service = CreateService(counter);
        var first = (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = seed.ActiveId, PageSize = 1 }, TestContext.Current.CancellationToken))
            .Value.ShouldBeOfType<CampaignEffectivePlacementsResult>();
        var firstReaders = counter.ReaderExecutionCount;
        first.Participants.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(seed.PlayerIds[0]);
        first.Counts.ShouldBe(new EffectivePlacementCounts(0, 12, 0, 0));

        var all = (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = seed.ActiveId, PageSize = 100 }, TestContext.Current.CancellationToken))
            .Value.ShouldBeOfType<CampaignEffectivePlacementsResult>();
        (counter.ReaderExecutionCount - firstReaders).ShouldBe(firstReaders);
        firstReaders.ShouldBeGreaterThan(0);
        all.Participants.Items.Select(row => row.PlayerId).ShouldBe(seed.PlayerIds);

        var second = (await service.GetCurrentSeasonRosterAsync(new() { Page = 2, PageSize = 5, GraduationYear = 2030 }, TestContext.Current.CancellationToken))
            .Value.ShouldBeOfType<CurrentSeasonRosterResult>();
        second.Roster.TotalCount.ShouldBe(12);
        second.Roster.Items.Select(row => row.PlayerId).ShouldBe(seed.PlayerIds.Skip(5).Take(5));
        var filtered = (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = seed.ActiveId, Search = "7", TeamId = seed.LatestTeamId }, TestContext.Current.CancellationToken))
            .Value.ShouldBeOfType<CampaignEffectivePlacementsResult>();
        filtered.Participants.Items.ShouldHaveSingleItem().TryoutNumber.ShouldBe(7);
        filtered.Participants.TotalCount.ShouldBe(1);
        filtered.Counts.ShouldBe(all.Counts);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task PostgreSqlSearchTreatsWildcardAndEscapeCharactersAsLiteralAsync(string literal)
    {
        var seed = await SeedAsync(2);
        await using var db = fixture.CreateAdminContext();
        var player = await db.Players.SingleAsync(p => p.PlayerId == seed.PlayerIds[0], TestContext.Current.CancellationToken);
        player.FirstName = $"Literal{literal}Name";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var user = fixture.UseUser(1, seed.ClubId, isClubAdmin: false);
        var service = CreateService();
        var roster = (await service.GetCurrentSeasonRosterAsync(new() { Search = literal }, TestContext.Current.CancellationToken)).Value.ShouldBeOfType<CurrentSeasonRosterResult>();
        roster.Roster.TotalCount.ShouldBe(1);
        roster.Roster.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(player.PlayerId);
        var working = (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = seed.ActiveId, Search = literal }, TestContext.Current.CancellationToken))
            .Value.ShouldBeOfType<CampaignEffectivePlacementsResult>();
        working.Participants.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(player.PlayerId);
        working.Counts.OptionalReassignment.ShouldBe(2);
    }

    [Fact]
    public async Task ClosedRosterRetainsOriginalAttributionTeamAndTokenAfterLaterDecisionAsync()
    {
        var seed = await SeedAsync(1);
        using var user = fixture.UseUser(1, seed.ClubId, isClubAdmin: false);
        var service = CreateService();
        var input = new GetClosedCampaignRosterInput { CampaignId = seed.LatestClosedId };
        var original = (await service.GetClosedCampaignRosterAsync(input, TestContext.Current.CancellationToken)).Value.ShouldBeOfType<ClosedCampaignRosterResult>();
        await using var db = fixture.CreateAdminContext();
        var current = await db.PlayerCampaignAssignments.SingleAsync(a => a.CampaignId == seed.ActiveId, TestContext.Current.CancellationToken);
        current.PlacementOutcome = PlacementOutcome.NotSelected;
        current.DecisionRecordedAt = DateTimeOffset.UtcNow;
        current.DecisionRecordedById = 2;
        current.DecisionActorDisplayName = "Later recorder";
        current.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var after = (await service.GetClosedCampaignRosterAsync(input, TestContext.Current.CancellationToken)).Value.ShouldBeOfType<ClosedCampaignRosterResult>();
        after.Participants.Items.ShouldHaveSingleItem().ShouldBe(original.Participants.Items.ShouldHaveSingleItem());
        after.Campaign.Status.ShouldBe(CampaignStatus.Closed);
        after.Participants.Items[0].Source.Decision.CampaignId.ShouldBe(seed.LatestClosedId);
        after.Participants.Items[0].Source.Team!.TeamId.ShouldBe(seed.LatestTeamId);
        var working = (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = seed.ActiveId }, TestContext.Current.CancellationToken))
            .Value.ShouldBeOfType<CampaignEffectivePlacementsResult>();
        working.Counts.ShouldBe(new EffectivePlacementCounts(0, 0, 1, 0));
        working.Participants.Items[0].LocalDecision!.ConcurrencyToken.ShouldBe(current.ConcurrencyToken);
    }

    [Fact]
    public async Task CurrentSeasonRosterSnapshotRetainsSeasonAndMembershipWhenSeasonAdvancesBetweenReadsAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(1);
        await PrepareClosedCampaignAsync(seed);
        using var user = fixture.UseUser(1, seed.ClubId, isClubAdmin: false);
        var original = (await CreateService().GetCurrentSeasonRosterAsync(new(), token)).Value.ShouldBeOfType<CurrentSeasonRosterResult>();
        var gate = new PlacementReadGateInterceptor("Seasons");
        var pendingRead = CreateService(gate).GetCurrentSeasonRosterAsync(new(), token);
        try
        {
            await gate.WaitUntilBlockedAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
            gate.CompletedIdentityRead.ShouldBeTrue();
            var nextSeasonId = await AdvanceSeasonAsync(seed.ClubId);
            var fresh = (await CreateService().GetCurrentSeasonRosterAsync(new(), token)).Value.ShouldBeOfType<CurrentSeasonRosterResult>();
            fresh.Season!.SeasonId.ShouldBe(nextSeasonId);
            fresh.Roster.TotalCount.ShouldBe(0);
            fresh.Roster.Items.ShouldBeEmpty();
        }
        finally
        {
            gate.Release();
        }

        var snapshot = (await pendingRead).Value.ShouldBeOfType<CurrentSeasonRosterResult>();
        snapshot.Season.ShouldBe(original.Season);
        snapshot.Season!.SeasonId.ShouldBe(seed.SeasonId);
        snapshot.Roster.TotalCount.ShouldBe(1);
        snapshot.Roster.Items.ShouldHaveSingleItem().ShouldBe(original.Roster.Items.ShouldHaveSingleItem());
    }

    [Fact]
    public async Task ActivePlacementSnapshotRetainsLifecycleAndWorkWhenCampaignClosesBetweenReadsAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(1);
        using var user = fixture.UseUser(1, seed.ClubId, isClubAdmin: false);
        var input = new GetCampaignEffectivePlacementsInput { CampaignId = seed.ActiveId };
        var original = (await CreateService().GetCampaignEffectivePlacementsAsync(input, token)).Value.ShouldBeOfType<CampaignEffectivePlacementsResult>();
        var gate = new PlacementReadGateInterceptor("Campaigns");
        var pendingRead = CreateService(gate).GetCampaignEffectivePlacementsAsync(input, token);
        try
        {
            await gate.WaitUntilBlockedAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
            gate.CompletedIdentityRead.ShouldBeTrue();
            await PrepareClosedCampaignAsync(seed);
            var fresh = await CreateService().GetCampaignEffectivePlacementsAsync(input, token);
            fresh.IsProblem.ShouldBeTrue();
            fresh.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        }
        finally
        {
            gate.Release();
        }

        var snapshot = (await pendingRead).Value.ShouldBeOfType<CampaignEffectivePlacementsResult>();
        snapshot.Campaign.ShouldBe(original.Campaign);
        snapshot.Campaign.Status.ShouldBe(CampaignStatus.Active);
        snapshot.Counts.ShouldBe(new EffectivePlacementCounts(0, 1, 0, 0));
        snapshot.Participants.TotalCount.ShouldBe(1);
        snapshot.Participants.Items.ShouldHaveSingleItem().ShouldBe(original.Participants.Items.ShouldHaveSingleItem());
        snapshot.Participants.Items[0].LocalDecision.ShouldBeNull();
    }

    [Fact]
    public async Task ClosedRosterSnapshotRetainsClosedLifecycleAndDecisionWhenReopenedBetweenReadsAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(1);
        await PrepareClosedCampaignAsync(seed);
        using var user = fixture.UseUser(1, seed.ClubId, isClubAdmin: false);
        var input = new GetClosedCampaignRosterInput { CampaignId = seed.ActiveId };
        var original = (await CreateService().GetClosedCampaignRosterAsync(input, token)).Value.ShouldBeOfType<ClosedCampaignRosterResult>();
        var gate = new PlacementReadGateInterceptor("Campaigns");
        var pendingRead = CreateService(gate).GetClosedCampaignRosterAsync(input, token);
        try
        {
            await gate.WaitUntilBlockedAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
            gate.CompletedIdentityRead.ShouldBeTrue();
            await ReopenAndReplaceDecisionAsync(seed.ActiveId);
            // The writer has committed, and the suspended read has already observed Closed.
            var fresh = await CreateService().GetClosedCampaignRosterAsync(input, token);
            fresh.IsProblem.ShouldBeTrue();
            fresh.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        }
        finally
        {
            gate.Release();
        }

        var snapshot = (await pendingRead).Value.ShouldBeOfType<ClosedCampaignRosterResult>();
        snapshot.Campaign.Status.ShouldBe(CampaignStatus.Closed);
        snapshot.Participants.TotalCount.ShouldBe(1);
        snapshot.Participants.Items.ShouldHaveSingleItem().ShouldBe(original.Participants.Items.ShouldHaveSingleItem());
        snapshot.Participants.Items[0].Source.Decision.Outcome.ShouldBe(PlacementOutcome.Assigned);
        snapshot.Participants.Items[0].Source.Decision.TeamId.ShouldBe(seed.LatestTeamId);
    }

    private async Task<long> AdvanceSeasonAsync(long clubId)
    {
        await using var db = fixture.CreateAdminContext();
        var nextSeason = new SeasonEntity
        {
            Name = "Next season",
            StartDate = new(2027, 1, 1),
            CreationOperationId = Guid.NewGuid(),
            ClubId = clubId,
            CreatedById = 1,
        };
        db.Seasons.Add(nextSeason);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var club = await db.Clubs.SingleAsync(c => c.ClubId == clubId, TestContext.Current.CancellationToken);
        club.CurrentSeasonId = nextSeason.SeasonId;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return nextSeason.SeasonId;
    }

    private async Task PrepareClosedCampaignAsync(Seed seed)
    {
        await using var db = fixture.CreateAdminContext();
        var assignment = await db.PlayerCampaignAssignments.Include(a => a.Campaign)
            .SingleAsync(a => a.CampaignId == seed.ActiveId, TestContext.Current.CancellationToken);
        assignment.PlacementOutcome = PlacementOutcome.Assigned;
        assignment.TeamId = seed.LatestTeamId;
        assignment.Campaign.Status = CampaignStatus.Closed;
        assignment.Campaign.ClosedAt = DateTimeOffset.UtcNow;
        assignment.Campaign.ClosedById = 1;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ReopenAndReplaceDecisionAsync(long campaignId)
    {
        await using var db = fixture.CreateAdminContext();
        var assignment = await db.PlayerCampaignAssignments.Include(a => a.Campaign)
            .SingleAsync(a => a.CampaignId == campaignId, TestContext.Current.CancellationToken);
        assignment.Campaign.Status = CampaignStatus.Active;
        assignment.Campaign.ClosedAt = null;
        assignment.Campaign.ClosedById = null;
        assignment.PlacementOutcome = PlacementOutcome.NotSelected;
        assignment.TeamId = null;
        assignment.DecisionRecordedById = 2;
        assignment.DecisionActorDisplayName = "Recorder after reopen";
        assignment.DecisionRecordedAt = DateTimeOffset.UtcNow;
        assignment.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private EffectivePlacementQueryService CreateService(DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<NovaReadDbContext>().UseNpgsql(fixture.ConnectionString)
            .UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance);
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }
        return new EffectivePlacementQueryService(new ReadFactory(options.Options, fixture.CurrentUser),
            fixture.CurrentUser, NullLogger<EffectivePlacementQueryService>.Instance);
    }

    private async Task<Seed> SeedAsync(int playerCount)
    {
        await using var db = fixture.CreateAdminContext();
        var club = new ClubEntity { Name = $"Effective {Guid.NewGuid():N}", City = "Austin", State = "TX", CreationOperationId = Guid.NewGuid(), CreatedById = 1 };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var season = new SeasonEntity { Name = "Current season", StartDate = new(2026, 1, 1), CreationOperationId = Guid.NewGuid(), ClubId = club.ClubId, CreatedById = 1 };
        var oldTeam = new TeamEntity { Name = "Old team", GraduationYear = 2030, CreationOperationId = Guid.NewGuid(), ClubId = club.ClubId, CreatedById = 1 };
        var latestTeam = new TeamEntity { Name = "Latest team", GraduationYear = 2030, CreationOperationId = Guid.NewGuid(), ClubId = club.ClubId, CreatedById = 1 };
        db.AddRange(season, oldTeam, latestTeam);
        var players = CreatePlayers(club.ClubId, playerCount);
        db.Players.AddRange(players);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        club.CurrentSeasonId = season.SeasonId;
        var campaigns = new List<CampaignEntity>();
        // Insert newer openings first so neither campaign nor participation IDs implement precedence.
        for (var sequence = 3; sequence >= 1; sequence--)
        {
            var campaign = new CampaignEntity
            {
                Name = $"Opening {sequence}",
                CreationOperationId = Guid.NewGuid(),
                SeasonId = season.SeasonId,
                ClubId = club.ClubId,
                CreatedById = 1,
                StartDate = new(2026, 6, 1),
                SeasonOpeningSequence = sequence,
                Status = sequence == 3 ? CampaignStatus.Active : CampaignStatus.Closed,
                ClosedAt = sequence == 3 ? null : DateTimeOffset.UtcNow,
                ClosedById = sequence == 3 ? null : 1,
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            campaigns.Add(campaign);
            var savedTeamId = sequence == 2 ? latestTeam.TeamId : oldTeam.TeamId;
            var recordedAt = new DateTimeOffset(2026, sequence == 2 ? 1 : 2, 1, 0, 0, 0, TimeSpan.Zero);
            db.PlayerCampaignAssignments.AddRange(players.Select((player, index) => new PlayerCampaignAssignmentEntity
            {
                CampaignId = campaign.CampaignId,
                PlayerId = player.PlayerId,
                ClubId = club.ClubId,
                CreatedById = 1,
                TryoutNumber = index + 1,
                PlacementOutcome = sequence == 3 ? PlacementOutcome.Undecided : PlacementOutcome.Assigned,
                TeamId = sequence == 3 ? null : savedTeamId,
                DecisionRecordedAt = sequence == 3 ? null : recordedAt,
                DecisionRecordedById = sequence == 3 ? null : 1,
                DecisionActorDisplayName = sequence == 3 ? null : "Original recorder",
            }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        return new(club.ClubId, season.SeasonId, campaigns[0].CampaignId, campaigns[1].CampaignId, oldTeam.TeamId, latestTeam.TeamId,
            players.Select(p => p.PlayerId).Order().ToArray());
    }

    private static PlayerEntity[] CreatePlayers(long clubId, int count)
        => Enumerable.Range(0, count).Select(_ => new PlayerEntity
        {
            FirstName = "Same",
            LastName = "Name",
            GraduationYear = 2030,
            DateOfBirth = new(2012, 1, 1),
            CreationOperationId = Guid.NewGuid(),
            ClubId = clubId,
            CreatedById = 1,
        }).ToArray();

    private sealed record Seed(long ClubId, long SeasonId, long ActiveId, long LatestClosedId, long OldTeamId, long LatestTeamId, long[] PlayerIds);

    private sealed class ReadFactory(DbContextOptions<NovaReadDbContext> options, FakeCurrentUserProvider user) : IDbContextFactory<NovaReadDbContext>
    {
        public NovaReadDbContext CreateDbContext() => new(options, user);
    }

    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        public int ReaderExecutionCount { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            ReaderExecutionCount++;
            return ValueTask.FromResult(result);
        }
    }

    /// <summary>Suspends the first participant query after EF has consumed the season or campaign identity row.</summary>
    private sealed class PlacementReadGateInterceptor(string identityTable) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _gated;
        public bool CompletedIdentityRead { get; private set; }

        public Task WaitUntilBlockedAsync(CancellationToken token) => _blocked.Task.WaitAsync(token);
        public void Release() => _release.TrySetResult();

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains($"FROM \"{identityTable}\"", StringComparison.Ordinal))
            {
                CompletedIdentityRead = true;
            }
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!_gated && CompletedIdentityRead && command.CommandText.Contains("FROM \"PlayerCampaignAssignments\"", StringComparison.Ordinal))
            {
                _gated = true;
                _blocked.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
