using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Features.Shared;
using Nova.Features.Tags;
using Nova.Features.Teams;
using Nova.Shared.Enums;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using OneOf;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Lifecycle;

/// <summary>
/// Tests administrator authorization, tenant isolation, archive provenance, and lifecycle integrity rules.
/// </summary>
public sealed class ArchivalLifecycleServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAAdminId = 200;
    private const long ClubAMemberId = 201;
    private const long ClubBAdminId = 202;
    private const long ActiveUndecidedPlayerId = 300;
    private const long ResolvedPlayerId = 301;
    private const long ClubBPlayerId = 302;
    private const long ActivePlacementTeamId = 400;
    private const long HistoricalPlacementTeamId = 401;
    private const long ClubBTeamId = 402;
    private const long TagDefinitionId = 500;
    private const long ClubBTagDefinitionId = 501;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes lifecycle test data for two clubs.
    /// </summary>
    public ArchivalLifecycleServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies player archive provenance is stamped and restore clears it without changing placement history.
    /// </summary>
    [Fact]
    public async Task PlayerLifecycle_ArchivesAndRestores_WithoutRewritingHistory()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreatePlayerService();

        var archiveResult = await service.ArchiveAsync(
            ResolvedPlayerId,
            TestContext.Current.CancellationToken);

        archiveResult.IsT0.ShouldBeTrue();

        await using (var archivedContext = _harness.CreateAdminContext())
        {
            var player = await archivedContext.Players
                .SingleAsync(candidate => candidate.PlayerId == ResolvedPlayerId, TestContext.Current.CancellationToken);
            player.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
            player.ArchivedAt.ShouldNotBeNull();
            player.ArchivedById.ShouldBe(ClubAAdminId);
            player.ModifiedAt.ShouldNotBeNull();
            player.ModifiedById.ShouldBe(ClubAAdminId);

            var outcomes = await archivedContext.PlayerCampaignAssignments
                .Where(assignment => assignment.PlayerId == ResolvedPlayerId)
                .Select(assignment => assignment.PlacementOutcome)
                .ToListAsync(TestContext.Current.CancellationToken);
            outcomes.ShouldBe([PlacementOutcome.Assigned, PlacementOutcome.Assigned], ignoreOrder: true);
        }

        var restoreResult = await service.RestoreAsync(
            ResolvedPlayerId,
            TestContext.Current.CancellationToken);

        restoreResult.IsT0.ShouldBeTrue();

        await using var restoredContext = _harness.CreateAdminContext();
        var restored = await restoredContext.Players
            .SingleAsync(candidate => candidate.PlayerId == ResolvedPlayerId, TestContext.Current.CancellationToken);
        restored.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        restored.ArchivedAt.ShouldBeNull();
        restored.ArchivedById.ShouldBeNull();
    }

    /// <summary>
    /// Verifies an undecided active-campaign participation blocks player archival without changing its outcome.
    /// </summary>
    [Fact]
    public async Task PlayerLifecycle_ReturnsConflict_ForUndecidedActiveCampaignParticipation()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreatePlayerService();

        var result = await service.ArchiveAsync(
            ActiveUndecidedPlayerId,
            TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var player = await verify.Players
            .SingleAsync(candidate => candidate.PlayerId == ActiveUndecidedPlayerId, TestContext.Current.CancellationToken);
        player.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        var outcome = await verify.PlayerCampaignAssignments
            .Where(assignment => assignment.PlayerId == ActiveUndecidedPlayerId)
            .Select(assignment => assignment.PlacementOutcome)
            .SingleAsync(TestContext.Current.CancellationToken);
        outcome.ShouldBe(PlacementOutcome.Undecided);
    }

    /// <summary>
    /// Verifies active placements block team archival while historical placements do not.
    /// </summary>
    [Fact]
    public async Task TeamLifecycle_ArchivesOnly_WhenNoActiveCampaignPlacementExists()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateTeamService();

        var blockedResult = await service.ArchiveAsync(
            ActivePlacementTeamId,
            TestContext.Current.CancellationToken);
        var archivedResult = await service.ArchiveAsync(
            HistoricalPlacementTeamId,
            TestContext.Current.CancellationToken);

        blockedResult.IsT3.ShouldBeTrue();
        archivedResult.IsT0.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var activeTeam = await verify.Teams
            .SingleAsync(candidate => candidate.TeamId == ActivePlacementTeamId, TestContext.Current.CancellationToken);
        var historicalTeam = await verify.Teams
            .SingleAsync(candidate => candidate.TeamId == HistoricalPlacementTeamId, TestContext.Current.CancellationToken);
        activeTeam.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        historicalTeam.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        historicalTeam.ArchivedById.ShouldBe(ClubAAdminId);
        (await verify.PlayerCampaignAssignments
            .AnyAsync(
                assignment => assignment.TeamId == HistoricalPlacementTeamId,
                TestContext.Current.CancellationToken))
            .ShouldBeTrue();
    }

    /// <summary>
    /// Verifies a team graduation-year change cannot make an active placement ineligible.
    /// </summary>
    [Fact]
    public async Task TeamLifecycle_ReturnsConflict_WhenGraduationYearInvalidatesActivePlacement()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateTeamService();

        var result = await service.UpdateGraduationYearAsync(
            new UpdateTeamGraduationYearInput(ActivePlacementTeamId, 2031),
            TestContext.Current.CancellationToken);

        result.IsT4.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var graduationYear = await verify.Teams
            .Where(team => team.TeamId == ActivePlacementTeamId)
            .Select(team => team.GraduationYear)
            .SingleAsync(TestContext.Current.CancellationToken);
        graduationYear.ShouldBe(2029);
    }

    /// <summary>
    /// Verifies a team graduation-year change succeeds when all active placements remain eligible.
    /// </summary>
    [Fact]
    public async Task TeamLifecycle_UpdatesGraduationYear_WhenActivePlacementsRemainEligible()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateTeamService();

        var result = await service.UpdateGraduationYearAsync(
            new UpdateTeamGraduationYearInput(ActivePlacementTeamId, 2030),
            TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var team = await verify.Teams
            .SingleAsync(candidate => candidate.TeamId == ActivePlacementTeamId, TestContext.Current.CancellationToken);
        team.GraduationYear.ShouldBe(2030);
        team.ModifiedById.ShouldBe(ClubAAdminId);
    }

    /// <summary>
    /// Verifies tag-definition archival preserves prior player associations and restore clears provenance.
    /// </summary>
    [Fact]
    public async Task TagDefinitionLifecycle_ArchivesAndRestores_WhilePreservingAssociations()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateTagService();

        var archiveResult = await service.ArchiveAsync(
            TagDefinitionId,
            TestContext.Current.CancellationToken);
        var restoreResult = await service.RestoreAsync(
            TagDefinitionId,
            TestContext.Current.CancellationToken);

        archiveResult.IsT0.ShouldBeTrue();
        restoreResult.IsT0.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var tag = await verify.PlayerTags
            .SingleAsync(candidate => candidate.PlayerTagId == TagDefinitionId, TestContext.Current.CancellationToken);
        tag.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        tag.ArchivedAt.ShouldBeNull();
        tag.ArchivedById.ShouldBeNull();

        var player = await verify.Players
            .Include(candidate => candidate.Tags)
            .SingleAsync(candidate => candidate.PlayerId == ResolvedPlayerId, TestContext.Current.CancellationToken);
        player.Tags.ShouldContain(candidate => candidate.PlayerTagId == TagDefinitionId);
    }

    /// <summary>
    /// Verifies regular club members cannot archive any lifecycle-managed record.
    /// </summary>
    /// <param name="target">The lifecycle-managed record type.</param>
    [Theory]
    [InlineData(LifecycleTarget.Player)]
    [InlineData(LifecycleTarget.Team)]
    [InlineData(LifecycleTarget.TagDefinition)]
    public async Task LifecycleMutation_ReturnsForbidden_WhenCallerIsNotClubAdmin(LifecycleTarget target)
    {
        ActAs(ClubAMemberId, ClubAId);

        var result = await ArchiveAsync(target, CurrentClubId(target));

        result.IsT2.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies tenant filters hide every cross-club lifecycle target.
    /// </summary>
    /// <param name="target">The lifecycle-managed record type.</param>
    [Theory]
    [InlineData(LifecycleTarget.Player)]
    [InlineData(LifecycleTarget.Team)]
    [InlineData(LifecycleTarget.TagDefinition)]
    public async Task LifecycleMutation_ReturnsNotFound_ForCrossTenantRecord(LifecycleTarget target)
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);

        var result = await ArchiveAsync(target, OtherClubId(target));

        result.IsT1.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies redundant lifecycle transitions return a conflict rather than silently succeeding.
    /// </summary>
    [Fact]
    public async Task LifecycleMutation_ReturnsConflict_WhenRecordAlreadyHasTargetStatus()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateTagService();

        var result = await service.RestoreAsync(
            TagDefinitionId,
            TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies archived records remain visible to tenant-scoped historical queries.
    /// </summary>
    [Fact]
    public async Task TenantQueries_IncludeArchivedLifecycleRecords_ForHistory()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        (await CreatePlayerService().ArchiveAsync(
            ResolvedPlayerId,
            TestContext.Current.CancellationToken)).IsT0.ShouldBeTrue();
        (await CreateTeamService().ArchiveAsync(
            HistoricalPlacementTeamId,
            TestContext.Current.CancellationToken)).IsT0.ShouldBeTrue();
        (await CreateTagService().ArchiveAsync(
            TagDefinitionId,
            TestContext.Current.CancellationToken)).IsT0.ShouldBeTrue();

        await using var db = _harness.CreateTenantContext();

        (await db.Players.AnyAsync(
            player => player.PlayerId == ResolvedPlayerId && player.LifecycleStatus == LifecycleStatus.Archived,
            TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await db.Teams.AnyAsync(
            team => team.TeamId == HistoricalPlacementTeamId && team.LifecycleStatus == LifecycleStatus.Archived,
            TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await db.PlayerTags.AnyAsync(
            tag => tag.PlayerTagId == TagDefinitionId && tag.LifecycleStatus == LifecycleStatus.Archived,
            TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    /// <summary>
    /// Archives one target through its focused lifecycle service.
    /// </summary>
    /// <param name="target">The target record type.</param>
    /// <param name="id">The target record identifier.</param>
    /// <returns>The common lifecycle mutation result.</returns>
    private Task<OneOf<Success, NotFound, LifecycleForbidden, LifecycleConflict>> ArchiveAsync(
        LifecycleTarget target,
        long id)
        => target switch
        {
            LifecycleTarget.Player => CreatePlayerService().ArchiveAsync(id, TestContext.Current.CancellationToken),
            LifecycleTarget.Team => CreateTeamService().ArchiveAsync(id, TestContext.Current.CancellationToken),
            LifecycleTarget.TagDefinition => CreateTagService().ArchiveAsync(id, TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    /// <summary>
    /// Gets the current-club record identifier for a lifecycle target.
    /// </summary>
    /// <param name="target">The target record type.</param>
    /// <returns>The current-club record identifier.</returns>
    private static long CurrentClubId(LifecycleTarget target)
        => target switch
        {
            LifecycleTarget.Player => ResolvedPlayerId,
            LifecycleTarget.Team => HistoricalPlacementTeamId,
            LifecycleTarget.TagDefinition => TagDefinitionId,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    /// <summary>
    /// Gets the other-club record identifier for a lifecycle target.
    /// </summary>
    /// <param name="target">The target record type.</param>
    /// <returns>The other-club record identifier.</returns>
    private static long OtherClubId(LifecycleTarget target)
        => target switch
        {
            LifecycleTarget.Player => ClubBPlayerId,
            LifecycleTarget.Team => ClubBTeamId,
            LifecycleTarget.TagDefinition => ClubBTagDefinitionId,
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

    /// <summary>
    /// Creates the player lifecycle service over the shared SQLite tenancy harness.
    /// </summary>
    /// <returns>A player lifecycle service using the mutable fake current-user provider.</returns>
    private PlayerLifecycleService CreatePlayerService()
        => new(
            CreateDbContextFactory(),
            _harness.CurrentUser,
            NullLogger<PlayerLifecycleService>.Instance);

    /// <summary>
    /// Creates the team lifecycle service over the shared SQLite tenancy harness.
    /// </summary>
    /// <returns>A team lifecycle service using the mutable fake current-user provider.</returns>
    private TeamLifecycleService CreateTeamService()
        => new(
            CreateDbContextFactory(),
            _harness.CurrentUser,
            NullLogger<TeamLifecycleService>.Instance);

    /// <summary>
    /// Creates the tag-definition lifecycle service over the shared SQLite tenancy harness.
    /// </summary>
    /// <returns>A tag-definition lifecycle service using the mutable fake current-user provider.</returns>
    private TagDefinitionLifecycleService CreateTagService()
        => new(
            CreateDbContextFactory(),
            _harness.CurrentUser,
            NullLogger<TagDefinitionLifecycleService>.Instance);

    /// <summary>
    /// Creates a tenant-scoped context factory for lifecycle services.
    /// </summary>
    /// <returns>A context factory backed by the shared SQLite connection.</returns>
    private IDbContextFactory<NovaDbContext> CreateDbContextFactory()
        => new TestDbContextFactory<NovaDbContext>(() => _harness.CreateTenantContext());

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
    /// Seeds lifecycle records, active and historical campaigns, placements, and a historical tag association.
    /// </summary>
    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity
            {
                ClubId = ClubAId,
                Name = "Club A",
                City = "Austin",
                State = "TX",
                CreatedById = ClubAAdminId
            },
            new ClubEntity
            {
                ClubId = ClubBId,
                Name = "Club B",
                City = "Boston",
                State = "MA",
                CreatedById = ClubBAdminId
            });

        db.Seasons.AddRange(
            new SeasonEntity
            {
                SeasonId = 600,
                Name = "Season A",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new SeasonEntity
            {
                SeasonId = 601,
                Name = "Season B",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.Campaigns.AddRange(
            new CampaignEntity
            {
                CampaignId = 700,
                Name = "Active Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = 600,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CampaignId = 701,
                Name = "Historical Campaign",
                StartDate = new DateOnly(2025, 6, 1),
                EndDate = new DateOnly(2025, 7, 1),
                SeasonId = 600,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CampaignId = 702,
                Name = "Club B Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = 601,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        var resolvedPlayer = new PlayerEntity
        {
            PlayerId = ResolvedPlayerId,
            FirstName = "Resolved",
            LastName = "Player",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        var tagDefinition = new PlayerTagEntity
        {
            PlayerTagId = TagDefinitionId,
            Name = "Fast",
            Color = "#ffffff",
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        resolvedPlayer.Tags.Add(tagDefinition);

        db.Players.AddRange(
            new PlayerEntity
            {
                PlayerId = ActiveUndecidedPlayerId,
                FirstName = "Undecided",
                LastName = "Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            resolvedPlayer,
            new PlayerEntity
            {
                PlayerId = ClubBPlayerId,
                FirstName = "Other",
                LastName = "Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.PlayerTags.Add(new PlayerTagEntity
        {
            PlayerTagId = ClubBTagDefinitionId,
            Name = "Other",
            Color = "#000000",
            ClubId = ClubBId,
            CreatedById = ClubBAdminId
        });

        db.Teams.AddRange(
            new TeamEntity
            {
                TeamId = ActivePlacementTeamId,
                Name = "Active Placement Team",
                GraduationYear = 2029,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                TeamId = HistoricalPlacementTeamId,
                Name = "Historical Placement Team",
                GraduationYear = 2029,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                TeamId = ClubBTeamId,
                Name = "Other Team",
                GraduationYear = 2029,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 800,
                PlayerId = ActiveUndecidedPlayerId,
                CampaignId = 700,
                PlacementOutcome = PlacementOutcome.Undecided,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 801,
                PlayerId = ResolvedPlayerId,
                CampaignId = 700,
                TeamId = ActivePlacementTeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 802,
                PlayerId = ResolvedPlayerId,
                CampaignId = 701,
                TeamId = HistoricalPlacementTeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            });

        db.SaveChanges();
    }

    /// <summary>
    /// Identifies which lifecycle-managed entity a matrix test targets.
    /// </summary>
    public enum LifecycleTarget
    {
        /// <summary>
        /// Targets a player.
        /// </summary>
        Player,

        /// <summary>
        /// Targets a team.
        /// </summary>
        Team,

        /// <summary>
        /// Targets a tag definition.
        /// </summary>
        TagDefinition,
    }
}
