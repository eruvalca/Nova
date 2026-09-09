using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class EffectivePlacementQueryServiceTests : IDisposable
{
    private const long ClubId = 1;
    private const long MemberId = 10;
    private const long SeasonId = 20;
    private const long PriorCampaignId = 100;
    private const long LaterCampaignId = 90;
    private const long ActiveCampaignId = 80;
    private const long TeamId = 30;
    private const long OtherTeamId = 31;
    private readonly TenancyTestHarness _harness = new();

    public EffectivePlacementQueryServiceTests()
    {
        Seed();
        ActAs();
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task SupplementalEnrollmentPreservesEffectiveAssignmentAndLocalTokenAsync()
    {
        var player = AddPlayer();
        var prior = AddDecision(player, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        var local = AddDecision(player, ActiveCampaignId, PlacementOutcome.Undecided);

        var roster = await RosterAsync();
        roster.Season.ShouldBe(new PlacementSeasonIdentity(SeasonId, "Current season"));
        var source = roster.Roster.Items.ShouldHaveSingleItem().Source;
        source.Decision.PlayerCampaignAssignmentId.ShouldBe(prior.PlayerCampaignAssignmentId);
        source.Decision.ConcurrencyToken.ShouldBe(prior.ConcurrencyToken);
        source.Decision.RecordedById.ShouldBe(MemberId);
        source.Decision.ActorDisplayName.ShouldBe("Original decision maker");
        source.Decision.RecordedAt.ShouldBe(prior.DecisionRecordedAt!.Value);
        source.Team!.TeamId.ShouldBe(TeamId);

        var work = await WorkAsync();
        work.Counts.ShouldBe(new EffectivePlacementCounts(0, 1, 0, 0));
        var item = work.Participants.Items.ShouldHaveSingleItem();
        item.LocalDecision.ShouldBeNull();
        item.ConcurrencyToken.ShouldBe(local.ConcurrencyToken);
        item.EffectiveDecision.ShouldBe(source);
        item.EffectiveTeam!.TeamId.ShouldBe(TeamId);
        item.Eligibility.ShouldBe(EffectivePlacementEligibility.OptionalReassignment);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Assigned, EffectivePlacementEligibility.OptionalReassignment)]
    [InlineData(PlacementOutcome.NotSelected, EffectivePlacementEligibility.NeedsPlacement)]
    [InlineData(PlacementOutcome.Withdrawn, EffectivePlacementEligibility.Unavailable)]
    public async Task LatestOpeningSequenceSupersedesOlderAssignmentWithoutFallbackAsync(
        PlacementOutcome outcome, EffectivePlacementEligibility eligibility)
    {
        var player = AddPlayer();
        AddDecision(player, PriorCampaignId, PlacementOutcome.Assigned, TeamId, DateTimeOffset.UnixEpoch.AddYears(30));
        var latest = AddDecision(player, LaterCampaignId, outcome,
            outcome == PlacementOutcome.Assigned ? OtherTeamId : null, DateTimeOffset.UnixEpoch);
        AddDecision(player, ActiveCampaignId, PlacementOutcome.Undecided);

        var roster = await RosterAsync();
        roster.Roster.TotalCount.ShouldBe(outcome == PlacementOutcome.Assigned ? 1 : 0);
        if (outcome == PlacementOutcome.Assigned)
        {
            roster.Roster.Items.ShouldHaveSingleItem().Source.Team!.TeamId.ShouldBe(OtherTeamId);
        }

        (await RosterAsync(new GetCurrentSeasonRosterInput { TeamId = TeamId })).Roster.Items.ShouldBeEmpty();
        var item = (await WorkAsync()).Participants.Items.ShouldHaveSingleItem();
        item.Eligibility.ShouldBe(eligibility);
        item.EffectiveDecision!.Decision.PlayerCampaignAssignmentId.ShouldBe(latest.PlayerCampaignAssignmentId);
        item.EffectiveDecision.Decision.SeasonOpeningSequence.ShouldBe(2);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithdrawnAndArchivedPlayersRemainUnavailableRegardlessOfOverrideAuthorityAsync(bool isAdmin)
    {
        var withdrawn = AddPlayer("Withdrawn");
        AddDecision(withdrawn, PriorCampaignId, PlacementOutcome.Withdrawn);
        AddDecision(withdrawn, ActiveCampaignId, PlacementOutcome.Undecided);
        var archived = AddPlayer("Archived", archived: true);
        AddDecision(archived, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(archived, ActiveCampaignId, PlacementOutcome.Undecided);
        ActAs(isAdmin);

        var work = await WorkAsync();
        work.Counts.ShouldBe(new EffectivePlacementCounts(0, 0, 0, 2));
        work.Participants.Items.Count.ShouldBe(2);
        work.Participants.Items.ShouldAllBe(item => item.Eligibility == EffectivePlacementEligibility.Unavailable);
        (await RosterAsync()).Roster.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task LocalNotSelectedResolvesWhilePriorNotSelectedAndFirstEnrollmentNeedPlacementAsync()
    {
        var local = AddPlayer("Local");
        AddDecision(local, ActiveCampaignId, PlacementOutcome.NotSelected);
        var prior = AddPlayer("Prior");
        AddDecision(prior, PriorCampaignId, PlacementOutcome.NotSelected);
        AddDecision(prior, ActiveCampaignId, PlacementOutcome.Undecided);
        var first = AddPlayer("First");
        AddDecision(first, ActiveCampaignId, PlacementOutcome.Undecided);

        var work = await WorkAsync();
        work.Counts.ShouldBe(new EffectivePlacementCounts(2, 0, 1, 0));
        work.Participants.Items.Single(item => item.PlayerId == local.PlayerId).Eligibility.ShouldBe(EffectivePlacementEligibility.Resolved);
        work.Participants.Items.Single(item => item.PlayerId == prior.PlayerId).Eligibility.ShouldBe(EffectivePlacementEligibility.NeedsPlacement);
        work.Participants.Items.Single(item => item.PlayerId == first.PlayerId).EffectiveDecision.ShouldBeNull();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementCorrectionReason.TeamArchived)]
    [InlineData(PlacementCorrectionReason.TeamIncompatible)]
    [InlineData(PlacementCorrectionReason.TeamUnavailable)]
    public async Task InvalidLatestAssignmentRetainsEvidenceAndNeedsPlacementWithoutFallbackAsync(PlacementCorrectionReason reason)
    {
        var player = AddPlayer();
        AddDecision(player, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        var latest = AddDecision(player, LaterCampaignId, PlacementOutcome.Assigned, OtherTeamId);
        AddDecision(player, ActiveCampaignId, PlacementOutcome.Undecided);
        using (var db = _harness.CreateAdminContext())
        {
            var team = db.Teams.Single(row => row.TeamId == OtherTeamId);
            if (reason == PlacementCorrectionReason.TeamArchived)
            {
                team.LifecycleStatus = LifecycleStatus.Archived;
                team.ArchivedAt = DateTimeOffset.UnixEpoch;
                team.ArchivedById = MemberId;
            }
            else if (reason == PlacementCorrectionReason.TeamIncompatible)
            {
                team.GraduationYear = 2029;
            }
            else
            {
                // Model a corrupt cross-tenant reference without disabling relational checks.
                team.ClubId = 2;
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await RosterAsync()).Roster.Items.ShouldBeEmpty();
        var work = await WorkAsync();
        work.Counts.NeedsPlacement.ShouldBe(1);
        var item = work.Participants.Items.ShouldHaveSingleItem();
        item.EffectiveTeam.ShouldBeNull();
        item.CorrectionReason.ShouldBe(reason);
        item.Eligibility.ShouldBe(EffectivePlacementEligibility.NeedsPlacement);
        item.EffectiveDecision!.Decision.PlayerCampaignAssignmentId.ShouldBe(latest.PlayerCampaignAssignmentId);
        item.EffectiveDecision.Decision.Outcome.ShouldBe(PlacementOutcome.Assigned);
    }

    [Fact]
    public async Task IdleCurrentSeasonStillHasEffectiveRosterAsync()
    {
        var player = AddPlayer();
        AddDecision(player, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        using (var db = _harness.CreateAdminContext())
        {
            db.Campaigns.Remove(db.Campaigns.Single(row => row.CampaignId == ActiveCampaignId));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await RosterAsync()).Roster.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(player.PlayerId);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentSeasonPointerControlsRosterRatherThanMostRecentCampaignAsync(bool advanceSeason)
    {
        AddDecision(AddPlayer(), PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        using (var db = _harness.CreateAdminContext())
        {
            db.Clubs.Single(row => row.ClubId == ClubId).CurrentSeasonId = advanceSeason ? 21 : null;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var roster = await RosterAsync();
        roster.Roster.TotalCount.ShouldBe(0);
        roster.Roster.Items.ShouldBeEmpty();
        (roster.Season?.SeasonId).ShouldBe(advanceSeason ? 21L : null);
        var work = await CreateService().GetCampaignEffectivePlacementsAsync(
            new() { CampaignId = ActiveCampaignId }, TestContext.Current.CancellationToken);
        work.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task ClosedRosterPreservesLocalOutcomeTeamAttributionAndTokenAfterSupersessionAsync()
    {
        var player = AddPlayer();
        var saved = AddDecision(player, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        var before = await ClosedAsync(PriorCampaignId);
        AddDecision(player, ActiveCampaignId, PlacementOutcome.Assigned, OtherTeamId);

        var after = await ClosedAsync(PriorCampaignId);
        after.Campaign.ShouldBe(before.Campaign);
        after.Participants.Items.ShouldBe(before.Participants.Items);
        var row = after.Participants.Items.ShouldHaveSingleItem();
        row.Source.Decision.Outcome.ShouldBe(PlacementOutcome.Assigned);
        row.Source.Decision.ConcurrencyToken.ShouldBe(saved.ConcurrencyToken);
        row.Source.Decision.RecordedAt.ShouldBe(saved.DecisionRecordedAt!.Value);
        row.Source.Team!.TeamId.ShouldBe(TeamId);
        row.TryoutNumber.ShouldBe(42);
        (await RosterAsync()).Roster.Items.ShouldHaveSingleItem().Source.Team!.TeamId.ShouldBe(OtherTeamId);
    }

    [Fact]
    public async Task WorkingFiltersPreserveUnfilteredCountsAndStablePageBoundariesAsync()
    {
        var first = AddPlayer("Sam", "Same");
        var second = AddPlayer("Sam", "Same");
        var olderYear = AddPlayer("Zoe", "Last", graduationYear: 2027);
        AddDecision(first, ActiveCampaignId, PlacementOutcome.Undecided);
        AddDecision(second, ActiveCampaignId, PlacementOutcome.Undecided);
        AddDecision(olderYear, ActiveCampaignId, PlacementOutcome.NotSelected);

        var all = await WorkAsync(new() { CampaignId = ActiveCampaignId, PageSize = 1 });
        all.Participants.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(olderYear.PlayerId);
        var page = await WorkAsync(new()
        {
            CampaignId = ActiveCampaignId,
            GraduationYear = 2028,
            Eligibility = "needsplacement",
            PageSize = 1,
            Page = 2
        });
        page.Counts.ShouldBe(new EffectivePlacementCounts(2, 0, 1, 0));
        page.Participants.TotalCount.ShouldBe(2);
        page.Participants.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(second.PlayerId);
        var beyond = await WorkAsync(new() { CampaignId = ActiveCampaignId, Page = 4, PageSize = 1 });
        beyond.Participants.Items.ShouldBeEmpty();
        beyond.Participants.TotalCount.ShouldBe(3);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task SearchTreatsSpecialCharactersAsLiteralSubstringsAsync(string search)
    {
        var literal = AddPlayer("Sam" + search, "Literal");
        AddDecision(literal, ActiveCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(AddPlayer("Samuel", "Other"), ActiveCampaignId, PlacementOutcome.Assigned, TeamId);

        var roster = await RosterAsync(new() { Search = search });
        roster.Roster.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(literal.PlayerId);
        var work = await WorkAsync(new() { CampaignId = ActiveCampaignId, Search = search });
        work.Participants.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(literal.PlayerId);
        work.Counts.OptionalReassignment.ShouldBe(2);
    }

    [Fact]
    public async Task TeamAndTryoutFiltersUseEffectiveAssignmentAndLocalEnrollmentAsync()
    {
        var player = AddPlayer();
        AddDecision(player, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(player, ActiveCampaignId, PlacementOutcome.Undecided, tryoutNumber: 987);
        AddDecision(AddPlayer("Other"), ActiveCampaignId, PlacementOutcome.Assigned, OtherTeamId);

        var work = await WorkAsync(new() { CampaignId = ActiveCampaignId, TeamId = TeamId, Search = "987" });
        work.Participants.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(player.PlayerId);
        work.Counts.OptionalReassignment.ShouldBe(2);
        var roster = await RosterAsync(new() { TeamId = TeamId, GraduationYear = 2028 });
        roster.Roster.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(player.PlayerId);
        (await RosterAsync(new() { GraduationYear = 2029 })).Roster.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task RosterPagingUsesPlayerIdToBreakDuplicateNamesAsync()
    {
        var first = AddPlayer();
        var second = AddPlayer();
        AddDecision(first, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(second, PriorCampaignId, PlacementOutcome.Assigned, TeamId);

        (await RosterAsync(new() { PageSize = 1 })).Roster.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(first.PlayerId);
        (await RosterAsync(new() { Page = 2, PageSize = 1 })).Roster.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(second.PlayerId);
        var closed = await CreateService().GetClosedCampaignRosterAsync(
            new() { CampaignId = PriorCampaignId, Page = 2, PageSize = 1 }, TestContext.Current.CancellationToken);
        closed.Value.Participants.Items.ShouldHaveSingleItem().PlayerId.ShouldBe(second.PlayerId);
        closed.Value.Participants.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task ReaderQueryCountDoesNotGrowWithParticipantCountAsync()
    {
        AddDecision(AddPlayer(), ActiveCampaignId, PlacementOutcome.Undecided);
        var small = new CountingCommandInterceptor();
        var first = await CreateService(small).GetCampaignEffectivePlacementsAsync(
            new() { CampaignId = ActiveCampaignId }, TestContext.Current.CancellationToken);
        first.IsSuccess.ShouldBeTrue();
        for (var index = 0; index < 12; index++)
        {
            var player = AddPlayer("Player" + index);
            AddDecision(player, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
            AddDecision(player, ActiveCampaignId, PlacementOutcome.Undecided);
        }

        var large = new CountingCommandInterceptor();
        var second = await CreateService(large).GetCampaignEffectivePlacementsAsync(
            new() { CampaignId = ActiveCampaignId }, TestContext.Current.CancellationToken);
        second.Value.Participants.TotalCount.ShouldBe(13);
        small.ReaderExecutionCount.ShouldBeGreaterThan(0);
        large.ReaderExecutionCount.ShouldBe(small.ReaderExecutionCount);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllReadsRejectAnonymousOrClublessUsersAsync(bool signedIn)
    {
        _harness.CurrentUser.UserId = signedIn ? MemberId : null;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();
        (await service.GetCurrentSeasonRosterAsync(new(), TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = ActiveCampaignId }, TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        (await service.GetClosedCampaignRosterAsync(new() { CampaignId = PriorCampaignId }, TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task CrossTenantIdentifiersRemainNonDisclosingAsync()
    {
        _harness.CurrentUser.UserId = 11;
        _harness.CurrentUser.ClubId = 2;
        var service = CreateService();
        (await service.GetCurrentSeasonRosterAsync(new() { TeamId = TeamId }, TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = ActiveCampaignId }, TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        (await service.GetClosedCampaignRosterAsync(new() { CampaignId = PriorCampaignId }, TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task CrossTenantPlayerReferencesCannotLeakThroughTenantOwnedParticipationsAsync()
    {
        var player = AddPlayer();
        AddDecision(player, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(player, ActiveCampaignId, PlacementOutcome.Undecided);
        using (var db = _harness.CreateAdminContext())
        {
            db.Players.Single(row => row.PlayerId == player.PlayerId).ClubId = 2;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await RosterAsync()).Roster.Items.ShouldBeEmpty();
        var work = await WorkAsync();
        work.Participants.Items.ShouldBeEmpty();
        work.Counts.ShouldBe(new EffectivePlacementCounts(0, 0, 0, 0));
        (await ClosedAsync(PriorCampaignId)).Participants.Items.ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, ServiceProblemKind.NotFound)]
    [InlineData(true, ServiceProblemKind.Conflict)]
    public async Task DraftVisibilityAndUnsupportedLifecycleAreDistinctAsync(bool isAdmin, ServiceProblemKind draftProblem)
    {
        ActAs(isAdmin);
        var service = CreateService();
        (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = 70 }, TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(draftProblem);
        (await service.GetClosedCampaignRosterAsync(new() { CampaignId = 70 }, TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(draftProblem);
        (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = PriorCampaignId }, TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        (await service.GetClosedCampaignRosterAsync(new() { CampaignId = ActiveCampaignId }, TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task AllReadsRejectInvalidPageBoundsAsync(int page, int pageSize)
    {
        var service = CreateService();
        (await service.GetCurrentSeasonRosterAsync(new() { Page = page, PageSize = pageSize }, TestContext.Current.CancellationToken))
            .Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = ActiveCampaignId, Page = page, PageSize = pageSize }, TestContext.Current.CancellationToken))
            .Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        (await service.GetClosedCampaignRosterAsync(new() { CampaignId = PriorCampaignId, Page = page, PageSize = pageSize }, TestContext.Current.CancellationToken))
            .Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    [Fact]
    public async Task EmptyPagesUseDocumentedDefaultAndMaximumBoundsAsync()
    {
        var roster = await RosterAsync();
        roster.Roster.Page.ShouldBe(1);
        roster.Roster.PageSize.ShouldBe(50);
        roster.Roster.TotalCount.ShouldBe(0);
        roster.Roster.Items.ShouldBeEmpty();
        var work = await WorkAsync();
        work.Participants.PageSize.ShouldBe(50);
        work.Counts.ShouldBe(new EffectivePlacementCounts(0, 0, 0, 0));
        (await ClosedAsync(PriorCampaignId)).Participants.PageSize.ShouldBe(50);
        (await RosterAsync(new() { PageSize = 100 })).Roster.PageSize.ShouldBe(100);
    }

    [Fact]
    public async Task DraftSavedRowsCannotSupersedeOpenedDecisionsEvenForAdministratorsAsync()
    {
        var player = AddPlayer();
        AddDecision(player, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(player, ActiveCampaignId, PlacementOutcome.Undecided);
        AddDecision(player, 70, PlacementOutcome.Withdrawn);
        ActAs(isAdmin: true);

        (await RosterAsync()).Roster.Items.ShouldHaveSingleItem().Source.Decision.CampaignId.ShouldBe(PriorCampaignId);
        (await WorkAsync()).Counts.ShouldBe(new EffectivePlacementCounts(0, 1, 0, 0));
    }

    [Fact]
    public async Task ClosedHistoryRetainsArchivedParticipantsAndTeamlessSavedOutcomesAsync()
    {
        var archived = AddPlayer("Archived", archived: true);
        AddDecision(archived, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(AddPlayer("Not selected"), PriorCampaignId, PlacementOutcome.NotSelected);
        AddDecision(AddPlayer("Withdrawn"), PriorCampaignId, PlacementOutcome.Withdrawn);

        var history = await ClosedAsync(PriorCampaignId);
        history.Participants.TotalCount.ShouldBe(3);
        history.Participants.Items.Single(item => item.PlayerId == archived.PlayerId).Source.Team!.TeamId.ShouldBe(TeamId);
        history.Participants.Items.Select(item => item.Source.Decision.Outcome)
            .ShouldBe([PlacementOutcome.Assigned, PlacementOutcome.NotSelected, PlacementOutcome.Withdrawn]);
        history.Participants.Items.Where(item => item.Source.Decision.Outcome != PlacementOutcome.Assigned)
            .ShouldAllBe(item => item.Source.Team == null && item.Source.Decision.TeamId == null);
    }

    [Fact]
    public async Task AdvancingSeasonResetsPriorWithdrawalToNeedsPlacementAsync()
    {
        var player = AddPlayer();
        AddDecision(player, PriorCampaignId, PlacementOutcome.Withdrawn);
        AddDecision(player, ActiveCampaignId, PlacementOutcome.Undecided);
        using (var db = _harness.CreateAdminContext())
        {
            db.Clubs.Single(row => row.ClubId == ClubId).CurrentSeasonId = 21;
            var campaign = db.Campaigns.Single(row => row.CampaignId == ActiveCampaignId);
            campaign.SeasonId = 21;
            campaign.SeasonOpeningSequence = 1;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var work = await WorkAsync();
        work.Campaign.Season.SeasonId.ShouldBe(21);
        work.Counts.ShouldBe(new EffectivePlacementCounts(1, 0, 0, 0));
        var item = work.Participants.Items.ShouldHaveSingleItem();
        item.EffectiveDecision.ShouldBeNull();
        item.Eligibility.ShouldBe(EffectivePlacementEligibility.NeedsPlacement);
    }

    [Fact]
    public async Task ClosedRosterRejectsIncompleteLocalRecordInsteadOfBorrowingLaterDecisionAsync()
    {
        var player = AddPlayer();
        AddDecision(player, PriorCampaignId, PlacementOutcome.Undecided);
        AddDecision(player, ActiveCampaignId, PlacementOutcome.Assigned, TeamId);

        var result = await CreateService().GetClosedCampaignRosterAsync(
            new() { CampaignId = PriorCampaignId }, TestContext.Current.CancellationToken);

        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    private EffectivePlacementQueryService CreateService(CountingCommandInterceptor? interceptor = null) => new(
        new TestDbContextFactory<NovaReadDbContext>(() => interceptor is null ? _harness.CreateReadContext() : _harness.CreateReadContext(interceptor)),
        _harness.CurrentUser, NullLogger<EffectivePlacementQueryService>.Instance);

    private async Task<CurrentSeasonRosterResult> RosterAsync(GetCurrentSeasonRosterInput? input = null)
    {
        var result = await CreateService().GetCurrentSeasonRosterAsync(input ?? new(), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private async Task<CampaignEffectivePlacementsResult> WorkAsync(GetCampaignEffectivePlacementsInput? input = null)
    {
        var result = await CreateService().GetCampaignEffectivePlacementsAsync(input ?? new() { CampaignId = ActiveCampaignId }, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private async Task<ClosedCampaignRosterResult> ClosedAsync(long campaignId)
    {
        var result = await CreateService().GetClosedCampaignRosterAsync(new() { CampaignId = campaignId }, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private void ActAs(bool isAdmin = false)
    {
        _harness.CurrentUser.UserId = MemberId;
        _harness.CurrentUser.ClubId = ClubId;
        _harness.CurrentUser.IsClubAdmin = isAdmin;
    }

    private PlayerEntity AddPlayer(string firstName = "Sam", string lastName = "Same", int graduationYear = 2028, bool archived = false)
    {
        using var db = _harness.CreateAdminContext();
        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            ClubId = ClubId,
            CreatedById = MemberId,
            FirstName = firstName,
            LastName = lastName,
            GraduationYear = graduationYear,
            DateOfBirth = new DateOnly(2010, 1, 1),
            LifecycleStatus = archived ? LifecycleStatus.Archived : LifecycleStatus.Active,
            ArchivedAt = archived ? DateTimeOffset.UnixEpoch : null,
            ArchivedById = archived ? MemberId : null
        };
        db.Players.Add(player);
        db.SaveChanges();
        return player;
    }

    private PlayerCampaignAssignmentEntity AddDecision(PlayerEntity player, long campaignId, PlacementOutcome outcome,
        long? teamId = null, DateTimeOffset? recordedAt = null, int? tryoutNumber = null)
    {
        using var db = _harness.CreateAdminContext();
        var saved = outcome != PlacementOutcome.Undecided;
        var row = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaignId,
            ClubId = ClubId,
            CreatedById = MemberId,
            PlacementOutcome = outcome,
            TeamId = teamId,
            TryoutNumber = tryoutNumber ?? 42 + db.PlayerCampaignAssignments.Count(existing => existing.CampaignId == campaignId),
            ConcurrencyToken = Guid.NewGuid(),
            DecisionRecordedAt = saved ? recordedAt ?? DateTimeOffset.UnixEpoch : null,
            DecisionRecordedById = saved ? MemberId : null,
            DecisionActorDisplayName = saved ? "Original decision maker" : null
        };
        db.PlayerCampaignAssignments.Add(row);
        db.SaveChanges();
        return row;
    }

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubId, Name = "Club A", City = "Austin", State = "TX", CreatedById = MemberId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = 2, Name = "Club B", City = "Boston", State = "MA", CreatedById = 11 });
        db.Users.AddRange(
            new NovaUserEntity { Id = MemberId, FirstName = "Member", LastName = "A", ClubId = ClubId },
            new NovaUserEntity { Id = 11, FirstName = "Member", LastName = "B", ClubId = 2 });
        db.Seasons.AddRange(
            new SeasonEntity { CreationOperationId = Guid.NewGuid(), SeasonId = SeasonId, ClubId = ClubId, Name = "Current season", StartDate = new DateOnly(2026, 1, 1), CreatedById = MemberId },
            new SeasonEntity { CreationOperationId = Guid.NewGuid(), SeasonId = 21, ClubId = ClubId, Name = "Next season", StartDate = new DateOnly(2027, 1, 1), CreatedById = MemberId });
        db.Teams.AddRange(
            new TeamEntity { CreationOperationId = Guid.NewGuid(), TeamId = TeamId, ClubId = ClubId, Name = "Alpha", GraduationYear = 2028, CreatedById = MemberId },
            new TeamEntity { CreationOperationId = Guid.NewGuid(), TeamId = OtherTeamId, ClubId = ClubId, Name = "Beta", GraduationYear = 2028, CreatedById = MemberId });
        db.SaveChanges();
        db.Clubs.Single(row => row.ClubId == ClubId).CurrentSeasonId = SeasonId;
        db.Campaigns.AddRange(
            Campaign(PriorCampaignId, CampaignStatus.Closed, 1),
            Campaign(LaterCampaignId, CampaignStatus.Closed, 2),
            Campaign(ActiveCampaignId, CampaignStatus.Active, 3),
            Campaign(70, CampaignStatus.Draft, null));
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
