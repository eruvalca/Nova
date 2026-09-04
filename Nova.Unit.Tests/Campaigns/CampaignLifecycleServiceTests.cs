using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests campaign close and reopen authorization, blockers, and append-only lifecycle history.
/// </summary>
public sealed class CampaignLifecycleServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAAdminId = 200;
    private const long ClubAMemberId = 201;
    private const long ClubBAdminId = 202;
    private const long ReadyCampaignId = 600;
    private const long BlockedCampaignId = 601;
    private const long ClosedCampaignId = 602;
    private const long ClubBCampaignId = 603;
    private const long CurrentSeasonAId = 500;
    private const long NextSeasonAId = 502;
    private const long EligibleTeamId = 800;
    private const long IneligibleTeamId = 801;
    private const long ArchivedTeamId = 802;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes seeded campaign lifecycle data for two clubs.
    /// </summary>
    public CampaignLifecycleServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies a club administrator can close a campaign when all close conditions succeed.
    /// </summary>
    [Fact]
    public async Task CloseAsync_ClosesCampaign_AndAppendsClosedEvent_WhenConditionsPass()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.CloseAsync(ReadyCampaignId, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var campaign = await verify.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == ReadyCampaignId, TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Closed);
        campaign.ClosedById.ShouldBe(ClubAAdminId);
        campaign.ClosedAt.ShouldNotBeNull();

        var events = await verify.ActivityEvents
            .Where(candidate => candidate.CampaignId == ReadyCampaignId)
            .OrderBy(candidate => candidate.ActivityEventId)
            .ToListAsync(TestContext.Current.CancellationToken);
        events.Count.ShouldBe(1);
        events[0].EventKind.ShouldBe(ActivityEventKind.CampaignClosed);
        events[0].ClubId.ShouldBe(ClubAId);
        events[0].CreatedById.ShouldBe(ClubAAdminId);
        events[0].PayloadJson.ShouldContain("\"campaignName\":\"Ready Campaign\"");
    }

    /// <summary>
    /// Verifies close returns every blocker condition and does not partially transition the campaign.
    /// </summary>
    [Fact]
    public async Task CloseAsync_ReturnsAllBlockers_AndLeavesCampaignActive_WhenConditionsFail()
    {
        await SetOnlyActiveCampaignAsync(BlockedCampaignId);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.CloseAsync(BlockedCampaignId, TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue();
        result.AsT3.Errors.ShouldContainKey("outcomes");
        result.AsT3.Errors.ShouldContainKey("eligibility");
        result.AsT3.Errors.ShouldContainKey("archivedTeams");

        await using var verify = _harness.CreateAdminContext();
        var campaign = await verify.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == BlockedCampaignId, TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.ClosedAt.ShouldBeNull();
        campaign.ClosedById.ShouldBeNull();
        (await verify.ActivityEvents
            .AnyAsync(candidate => candidate.CampaignId == BlockedCampaignId, TestContext.Current.CancellationToken))
            .ShouldBeFalse();
    }

    /// <summary>
    /// Verifies reopening clears closure metadata, appends a reopen event, and preserves participation outcomes.
    /// </summary>
    [Fact]
    public async Task ReopenAsync_ClearsClosureMetadata_AndAppendsReopenedEvent()
    {
        await SetOnlyActiveCampaignAsync(null);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ReopenAsync(ClosedCampaignId, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var campaign = await verify.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == ClosedCampaignId, TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.ClosedAt.ShouldBeNull();
        campaign.ClosedById.ShouldBeNull();

        var events = await verify.ActivityEvents
            .Where(candidate => candidate.CampaignId == ClosedCampaignId)
            .OrderBy(candidate => candidate.ActivityEventId)
            .ToListAsync(TestContext.Current.CancellationToken);
        events.Count.ShouldBe(2);
        events[0].EventKind.ShouldBe(ActivityEventKind.CampaignClosed);
        events[1].EventKind.ShouldBe(ActivityEventKind.CampaignReopened);

        var outcomes = await verify.PlayerCampaignAssignments
            .Where(candidate => candidate.CampaignId == ClosedCampaignId)
            .Select(candidate => candidate.PlacementOutcome)
            .Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);
        outcomes.ShouldBe([PlacementOutcome.Assigned], ignoreOrder: true);
    }

    /// <summary>
    /// Verifies repeated close and reopen cycles retain every lifecycle event in order.
    /// </summary>
    [Fact]
    public async Task LifecycleTransitions_PreserveAllEvents_AcrossRepeatedCloseReopenCycles()
    {
        await SetOnlyActiveCampaignAsync(null);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        (await service.ReopenAsync(
            ClosedCampaignId,
            TestContext.Current.CancellationToken)).IsT0.ShouldBeTrue();
        (await service.CloseAsync(
            ClosedCampaignId,
            TestContext.Current.CancellationToken)).IsT0.ShouldBeTrue();
        (await service.ReopenAsync(
            ClosedCampaignId,
            TestContext.Current.CancellationToken)).IsT0.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var campaign = await verify.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == ClosedCampaignId, TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.ClosedAt.ShouldBeNull();
        campaign.ClosedById.ShouldBeNull();

        var eventTypes = await verify.ActivityEvents
            .Where(candidate => candidate.CampaignId == ClosedCampaignId)
            .OrderBy(candidate => candidate.ActivityEventId)
            .Select(candidate => candidate.EventKind)
            .ToListAsync(TestContext.Current.CancellationToken);
        eventTypes.ShouldBe(
        [
            ActivityEventKind.CampaignClosed,
            ActivityEventKind.CampaignReopened,
            ActivityEventKind.CampaignClosed,
            ActivityEventKind.CampaignReopened
        ]);
    }

    /// <summary>Verifies a Draft cannot be closed and produces no activity.</summary>
    [Fact]
    public async Task CloseAsync_ReturnsConflict_ForDraftCampaign()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);

        var result = await CreateService().CloseAsync(
            BlockedCampaignId,
            TestContext.Current.CancellationToken);

        result.IsT4.ShouldBeTrue();
        result.AsT4.Detail.ShouldContain("active campaign");
        await using var verify = _harness.CreateAdminContext();
        (await verify.ActivityEvents.CountAsync(
            activity => activity.CampaignId == BlockedCampaignId,
            TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    /// <summary>Verifies a Draft cannot be reopened and produces no activity.</summary>
    [Fact]
    public async Task ReopenAsync_ReturnsConflict_ForDraftCampaign()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);

        var result = await CreateService().ReopenAsync(
            BlockedCampaignId,
            TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue();
        result.AsT3.Detail.ShouldContain("closed campaign");
        await using var verify = _harness.CreateAdminContext();
        (await verify.ActivityEvents.CountAsync(
            activity => activity.CampaignId == BlockedCampaignId,
            TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    /// <summary>Verifies reopening conflicts while another campaign is Active.</summary>
    [Fact]
    public async Task ReopenAsync_ReturnsConflict_WhenClubAlreadyHasActiveCampaign()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);

        var result = await CreateService().ReopenAsync(
            ClosedCampaignId,
            TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue();
        result.AsT3.Detail.ShouldBe("Another campaign is already active for this club.");
    }

    /// <summary>
    /// Verifies non-admin users cannot close campaigns.
    /// </summary>
    [Fact]
    public async Task CloseAsync_ReturnsForbidden_WhenCallerIsNotClubAdmin()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.CloseAsync(ReadyCampaignId, TestContext.Current.CancellationToken);

        result.IsT2.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies tenant filters hide another club's campaign from close operations.
    /// </summary>
    [Fact]
    public async Task CloseAsync_ReturnsNotFound_ForCrossTenantCampaign()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.CloseAsync(ClubBCampaignId, TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies non-admin users cannot reopen campaigns.
    /// </summary>
    [Fact]
    public async Task ReopenAsync_ReturnsForbidden_WhenCallerIsNotClubAdmin()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ReopenAsync(ClosedCampaignId, TestContext.Current.CancellationToken);

        result.IsT2.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies tenant filters hide another club's campaign from reopen operations.
    /// </summary>
    [Fact]
    public async Task ReopenAsync_ReturnsNotFound_ForCrossTenantCampaign()
    {
        ActAs(ClubBAdminId, ClubBId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ReopenAsync(ClosedCampaignId, TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
    }

    /// <summary>Verifies a campaign from a superseded season cannot be reopened.</summary>
    [Fact]
    public async Task ReopenAsync_ReturnsConflict_WhenCampaignSeasonIsHistorical()
    {
        await using (var db = _harness.CreateAdminContext())
        {
            db.Clubs.Single(club => club.ClubId == ClubAId).CurrentSeasonId = NextSeasonAId;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var result = await CreateService().ReopenAsync(
            ClosedCampaignId,
            TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue();
        result.AsT3.Detail.ShouldContain("current season");

        await using var verify = _harness.CreateAdminContext();
        (await verify.Campaigns.SingleAsync(
            campaign => campaign.CampaignId == ClosedCampaignId,
            TestContext.Current.CancellationToken)).Status.ShouldBe(CampaignStatus.Closed);
        (await verify.ActivityEvents.CountAsync(
            activityEvent => activityEvent.CampaignId == ClosedCampaignId,
            TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// Verifies the cross-tier interface returns a success result when a close succeeds.
    /// </summary>
    [Fact]
    public async Task ICampaignLifecycleService_CloseAsync_ReturnsSuccess_WhenCloseSucceeds()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        ICampaignLifecycleService service = CreateService();

        var result = await service.CloseAsync(ReadyCampaignId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies the cross-tier interface maps close blockers to a conflict problem that preserves the
    /// condition-keyed error groups.
    /// </summary>
    [Fact]
    public async Task ICampaignLifecycleService_CloseAsync_MapsBlockers_ToConflictWithErrors()
    {
        await SetOnlyActiveCampaignAsync(BlockedCampaignId);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        ICampaignLifecycleService service = CreateService();

        var result = await service.CloseAsync(BlockedCampaignId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors!.ShouldContainKey("outcomes");
        result.Problem.Errors.ShouldContainKey("eligibility");
        result.Problem.Errors.ShouldContainKey("archivedTeams");
    }

    /// <summary>
    /// Verifies the cross-tier interface maps forbidden, not-found, and already-closed outcomes for close.
    /// </summary>
    [Fact]
    public async Task ICampaignLifecycleService_CloseAsync_MapsForbiddenNotFoundAndConflict()
    {
        ActAs(ClubAMemberId, ClubAId);
        ICampaignLifecycleService memberService = CreateService();
        var forbidden = await memberService.CloseAsync(ReadyCampaignId, TestContext.Current.CancellationToken);
        forbidden.IsProblem.ShouldBeTrue();
        forbidden.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);

        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        ICampaignLifecycleService adminService = CreateService();
        var notFound = await adminService.CloseAsync(ClubBCampaignId, TestContext.Current.CancellationToken);
        notFound.IsProblem.ShouldBeTrue();
        notFound.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        var alreadyClosed = await adminService.CloseAsync(ClosedCampaignId, TestContext.Current.CancellationToken);
        alreadyClosed.IsProblem.ShouldBeTrue();
        alreadyClosed.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        alreadyClosed.Problem.Errors.ShouldBeNull();
    }

    /// <summary>
    /// Verifies the cross-tier interface maps success, conflict, forbidden, and not-found outcomes for reopen.
    /// </summary>
    [Fact]
    public async Task ICampaignLifecycleService_ReopenAsync_MapsSuccessConflictForbiddenAndNotFound()
    {
        await SetOnlyActiveCampaignAsync(null);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        ICampaignLifecycleService adminService = CreateService();

        var success = await adminService.ReopenAsync(ClosedCampaignId, TestContext.Current.CancellationToken);
        success.IsSuccess.ShouldBeTrue();

        var alreadyActive = await adminService.ReopenAsync(ClosedCampaignId, TestContext.Current.CancellationToken);
        alreadyActive.IsProblem.ShouldBeTrue();
        alreadyActive.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        alreadyActive.Problem.Errors.ShouldBeNull();

        var notFound = await adminService.ReopenAsync(ClubBCampaignId, TestContext.Current.CancellationToken);
        notFound.IsProblem.ShouldBeTrue();
        notFound.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        ActAs(ClubAMemberId, ClubAId);
        ICampaignLifecycleService memberService = CreateService();
        var forbidden = await memberService.ReopenAsync(ClosedCampaignId, TestContext.Current.CancellationToken);
        forbidden.IsProblem.ShouldBeTrue();
        forbidden.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies opening persists an immutable receipt, enrolls only the tenant's active roster,
    /// and replays without duplicating participation or activity.
    /// </summary>
    [Fact]
    public async Task OpenAsync_OpensDraftAndReplaysOriginalReceipt_WithoutDuplicateEffects()
    {
        await using (var arrange = _harness.CreateAdminContext())
        {
            var legacyParticipation = await arrange.PlayerCampaignAssignments.SingleAsync(
                assignment => assignment.CampaignId == ClubBCampaignId,
                TestContext.Current.CancellationToken);
            arrange.PlayerCampaignAssignments.Remove(legacyParticipation);
            await arrange.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        ActAs(ClubBAdminId, ClubBId, isClubAdmin: true);
        var service = CreateService();
        var operationId = Guid.NewGuid();
        var input = new OpenCampaignInput { OperationId = operationId };

        var first = await service.OpenAsync(
            ClubBCampaignId,
            input,
            TestContext.Current.CancellationToken);
        var replay = await service.OpenAsync(
            ClubBCampaignId,
            input,
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();
        replay.Value.ShouldBe(first.Value);
        first.Value.OperationId.ShouldBe(operationId);
        first.Value.CampaignId.ShouldBe(ClubBCampaignId);
        first.Value.EnrolledPlayerCount.ShouldBe(1);
        first.Value.ActiveTeamCount.ShouldBe(1);
        first.Value.Warnings.ShouldBeEmpty();

        await using var verify = _harness.CreateAdminContext();
        var campaign = await verify.Campaigns.SingleAsync(
            candidate => candidate.CampaignId == ClubBCampaignId,
            TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.OpeningOperationId.ShouldBe(operationId);
        campaign.OpenedById.ShouldBe(ClubBAdminId);
        campaign.OpenedAt.ShouldBe(first.Value.OpenedAt);
        campaign.InitialEnrolledPlayerCount.ShouldBe(1);
        campaign.InitialActiveTeamCount.ShouldBe(1);
        campaign.SeasonOpeningSequence.ShouldBe(1);

        (await verify.PlayerCampaignAssignments.CountAsync(
            assignment => assignment.CampaignId == ClubBCampaignId,
            TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.ActivityEvents.CountAsync(
            activity => activity.CampaignId == ClubBCampaignId
                && activity.EventKind == ActivityEventKind.CampaignOpened,
            TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.ActivityEvents.AnyAsync(
            activity => activity.CampaignId == ClubBCampaignId
                && activity.EventKind >= ActivityEventKind.PlacementAssigned
                && activity.EventKind <= ActivityEventKind.PlacementSuperseded,
            TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    /// <summary>Verifies every freshly detected opening blocker is returned without writes.</summary>
    [Fact]
    public async Task OpenAsync_ReturnsAllBlockers_WithoutPartialMutation()
    {
        await using (var arrange = _harness.CreateAdminContext())
        {
            var players = await arrange.Players
                .Where(player => player.ClubId == ClubAId)
                .ToListAsync(TestContext.Current.CancellationToken);
            foreach (var player in players)
            {
                player.LifecycleStatus = LifecycleStatus.Archived;
                player.ArchivedAt = DateTimeOffset.UtcNow;
                player.ArchivedById = ClubAAdminId;
            }

            await arrange.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var operationId = Guid.NewGuid();
        var result = await CreateService().OpenAsync(
            BlockedCampaignId,
            new OpenCampaignInput { OperationId = operationId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Errors!.ShouldContainKey(CampaignOpeningProblemKeys.NoActivePlayers);
        result.Problem.Errors!.ShouldContainKey(CampaignOpeningProblemKeys.AnotherCampaignActive);
        result.Problem.Errors!.ShouldContainKey(CampaignOpeningProblemKeys.BlockingCampaignId);
        result.Problem.Errors!.ShouldContainKey(CampaignOpeningProblemKeys.BlockingCampaignName);

        await using var verify = _harness.CreateAdminContext();
        var campaign = await verify.Campaigns.SingleAsync(
            candidate => candidate.CampaignId == BlockedCampaignId,
            TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Draft);
        campaign.OpeningOperationId.ShouldBeNull();
        (await verify.ActivityEvents.AnyAsync(
            activity => activity.CampaignId == BlockedCampaignId
                && activity.EventKind == ActivityEventKind.CampaignOpened,
            TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    /// <summary>
    /// Verifies Draft deletion leaves durable club data and exactly one tenant-scoped tombstone.
    /// </summary>
    [Fact]
    public async Task DeleteDraftAsync_DeletesDraftAndReplaysFromDurableTombstone()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var first = await service.DeleteDraftAsync(
            BlockedCampaignId,
            TestContext.Current.CancellationToken);
        var replay = await service.DeleteDraftAsync(
            BlockedCampaignId,
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        replay.IsSuccess.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        (await verify.Campaigns.AnyAsync(
            campaign => campaign.CampaignId == BlockedCampaignId,
            TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.Seasons.AnyAsync(
            season => season.SeasonId == CurrentSeasonAId,
            TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await verify.Teams.CountAsync(
            team => team.ClubId == ClubAId,
            TestContext.Current.CancellationToken)).ShouldBe(3);
        var tombstones = await verify.ActivityEvents
            .Where(activity => activity.CampaignId == BlockedCampaignId
                && activity.EventKind == ActivityEventKind.CampaignDraftDeleted)
            .ToListAsync(TestContext.Current.CancellationToken);
        tombstones.Count.ShouldBe(1);
        tombstones[0].IsAdminOnly.ShouldBeTrue();
        tombstones[0].PayloadJson.ShouldContain("\"campaignName\":\"Blocked Campaign\"");
    }

    /// <summary>Verifies only the most recently opened current-season campaign may reopen.</summary>
    [Fact]
    public async Task ReopenAsync_RejectsOlderCampaign_WhenANewerCampaignWasOpened()
    {
        await SetOnlyActiveCampaignAsync(null);
        await using (var arrange = _harness.CreateAdminContext())
        {
            var older = await arrange.Campaigns.SingleAsync(
                campaign => campaign.CampaignId == ClosedCampaignId,
                TestContext.Current.CancellationToken);
            older.SeasonOpeningSequence = 10_000;
            arrange.Campaigns.Add(new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = "Newer Closed Campaign",
                StartDate = new DateOnly(2026, 8, 1),
                Status = CampaignStatus.Closed,
                OpeningOperationId = Guid.NewGuid(),
                OpenedAt = DateTimeOffset.UtcNow.AddDays(-2),
                OpenedById = ClubAAdminId,
                SeasonOpeningSequence = 10_001,
                InitialEnrolledPlayerCount = 0,
                InitialActiveTeamCount = 0,
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ClosedById = ClubAAdminId,
                SeasonId = CurrentSeasonAId,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId,
            });
            await arrange.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var result = await CreateService().ReopenAsync(
            ClosedCampaignId,
            TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue();
        result.AsT3.Detail.ShouldContain("most recently opened");
        await using var verify = _harness.CreateAdminContext();
        (await verify.Campaigns
            .Where(campaign => campaign.CampaignId == ClosedCampaignId)
            .Select(campaign => campaign.Status)
            .SingleAsync(TestContext.Current.CancellationToken)).ShouldBe(CampaignStatus.Closed);
    }

    /// <summary>
    /// Creates the campaign lifecycle service over the shared SQLite tenancy harness.
    /// </summary>
    /// <returns>A service instance using the mutable fake current-user provider.</returns>
    private CampaignLifecycleService CreateService()
    {
        IDbContextFactory<NovaDbContext> dbContextFactory =
            new TestDbContextFactory<NovaDbContext>(_harness.CreateTenantContext);

        return new CampaignLifecycleService(
            dbContextFactory,
            _harness.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);
    }

    /// <summary>
    /// Sets the current user state for the next tenant context.
    /// </summary>
    /// <param name="userId">The current user identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="isClubAdmin">Whether the current user is a club administrator.</param>
    private void ActAs(long userId, long clubId, bool isClubAdmin = false)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>
    /// Seeds campaign lifecycle data across two clubs.
    /// </summary>
    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                ClubId = ClubAId,
                Name = "Club A",
                City = "Austin",
                State = "TX",
                CreatedById = ClubAAdminId
            },
            new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                ClubId = ClubBId,
                Name = "Club B",
                City = "Boston",
                State = "MA",
                CreatedById = ClubBAdminId
            });

        db.Seasons.AddRange(
            new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                SeasonId = CurrentSeasonAId,
                Name = "Season A",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                SeasonId = 501,
                Name = "Season B",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            },
            new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                SeasonId = NextSeasonAId,
                Name = "Next Season A",
                StartDate = new DateOnly(2027, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            });

        db.Campaigns.AddRange(
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = ReadyCampaignId,
                Name = "Ready Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = CurrentSeasonAId,
                ClubId = ClubAId,
                Status = CampaignStatus.Active,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = BlockedCampaignId,
                Name = "Blocked Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = CurrentSeasonAId,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = ClosedCampaignId,
                Name = "Closed Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                Status = CampaignStatus.Closed,
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ClosedById = ClubAAdminId,
                SeasonId = CurrentSeasonAId,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = ClubBCampaignId,
                Name = "Club B Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = 501,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.Players.AddRange(
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = 700,
                FirstName = "Ready",
                LastName = "Assigned",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = 701,
                FirstName = "Ready",
                LastName = "Final",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = 702,
                FirstName = "Blocked",
                LastName = "Undecided",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = 703,
                FirstName = "Blocked",
                LastName = "Ineligible",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2029,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = 704,
                FirstName = "Blocked",
                LastName = "ArchivedTeam",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = 705,
                FirstName = "Closed",
                LastName = "Campaign",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerId = 706,
                FirstName = "ClubB",
                LastName = "Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.Teams.AddRange(
            new TeamEntity
            {
                CreationOperationId = Guid.NewGuid(),
                TeamId = EligibleTeamId,
                Name = "Eligible Team",
                GraduationYear = 2029,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                CreationOperationId = Guid.NewGuid(),
                TeamId = IneligibleTeamId,
                Name = "Ineligible Team",
                GraduationYear = 2031,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                CreationOperationId = Guid.NewGuid(),
                TeamId = ArchivedTeamId,
                Name = "Archived Team",
                GraduationYear = 2029,
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ArchivedById = ClubAAdminId,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                CreationOperationId = Guid.NewGuid(),
                TeamId = 803,
                Name = "Club B Team",
                GraduationYear = 2029,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 900,
                PlayerId = 700,
                CampaignId = ReadyCampaignId,
                TeamId = EligibleTeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 901,
                PlayerId = 701,
                CampaignId = ReadyCampaignId,
                PlacementOutcome = PlacementOutcome.NotSelected,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 902,
                PlayerId = 702,
                CampaignId = BlockedCampaignId,
                PlacementOutcome = PlacementOutcome.Undecided,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 903,
                PlayerId = 703,
                CampaignId = BlockedCampaignId,
                TeamId = IneligibleTeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 904,
                PlayerId = 704,
                CampaignId = BlockedCampaignId,
                TeamId = ArchivedTeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 905,
                PlayerId = 705,
                CampaignId = ClosedCampaignId,
                TeamId = EligibleTeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 906,
                PlayerId = 706,
                CampaignId = ClubBCampaignId,
                PlacementOutcome = PlacementOutcome.NotSelected,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.ActivityEvents.Add(new ActivityEventEntity
        {
            ActivityEventId = 1000,
            CampaignId = ClosedCampaignId,
            EventKind = ActivityEventKind.CampaignClosed,
            IsAdminOnly = false,
            ClubId = ClubAId,
            ActorUserId = ClubAAdminId,
            ActorDisplayName = "Club Admin",
            PayloadJson = """{"campaignId":602,"campaignName":"Closed Campaign"}""",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
            CreatedById = ClubAAdminId
        });

        db.SaveChanges();
        db.Clubs.Single(club => club.ClubId == ClubAId).CurrentSeasonId = CurrentSeasonAId;
        db.Clubs.Single(club => club.ClubId == ClubBId).CurrentSeasonId = 501;
        db.SaveChanges();
    }

    /// <summary>Chooses the sole Active campaign for a lifecycle scenario.</summary>
    /// <param name="campaignId">The campaign to activate, or <see langword="null"/> for none.</param>
    private async Task SetOnlyActiveCampaignAsync(long? campaignId)
    {
        await using var db = _harness.CreateAdminContext();
        var campaigns = await db.Campaigns
            .Where(campaign => campaign.ClubId == ClubAId && campaign.Status != CampaignStatus.Closed)
            .ToListAsync(TestContext.Current.CancellationToken);
        foreach (var campaign in campaigns.Where(campaign => campaign.Status == CampaignStatus.Active))
        {
            campaign.Status = CampaignStatus.Draft;
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        if (campaignId is long selectedCampaignId)
        {
            campaigns.Single(campaign => campaign.CampaignId == selectedCampaignId).Status = CampaignStatus.Active;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }
}
