using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests administrator authorization, placement integrity, tenant isolation, and optimistic concurrency.
/// </summary>
public sealed class CampaignPlacementServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAAdminId = 200;
    private const long ClubAMemberId = 201;
    private const long ClubBAdminId = 202;
    private const long ClubAAssignmentId = 300;
    private const long ClubBAssignmentId = 301;
    private const long EligibleTeamId = 400;
    private const long IneligibleTeamId = 401;
    private const long ClubBTeamId = 402;

    private readonly Guid _clubAConcurrencyToken = Guid.NewGuid();
    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes seeded campaign participation data for each test.
    /// </summary>
    public CampaignPlacementServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies a club administrator can assign an eligible player and receives a new token.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsync_AssignsEligiblePlayer_AndRegeneratesConcurrencyToken()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(
                ClubAAssignmentId,
                PlacementOutcome.Assigned,
                EligibleTeamId,
                _clubAConcurrencyToken),
            TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();
        result.AsT0.ConcurrencyToken.ShouldNotBe(_clubAConcurrencyToken);

        await using var verify = _harness.CreateAdminContext();
        var participation = await verify.PlayerCampaignAssignments
            .SingleAsync(
                assignment => assignment.PlayerCampaignAssignmentId == ClubAAssignmentId,
                TestContext.Current.CancellationToken);
        participation.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        participation.TeamId.ShouldBe(EligibleTeamId);
        participation.ConcurrencyToken.ShouldBe(result.AsT0.ConcurrencyToken);
    }

    /// <summary>
    /// Verifies every invalid outcome/team combination is rejected before database access.
    /// </summary>
    /// <param name="outcome">The invalid placement outcome.</param>
    /// <param name="teamId">The invalid team value for the outcome.</param>
    [Theory]
    [InlineData(PlacementOutcome.Assigned, null)]
    [InlineData(PlacementOutcome.Undecided, EligibleTeamId)]
    [InlineData(PlacementOutcome.NotSelected, EligibleTeamId)]
    [InlineData(PlacementOutcome.Withdrawn, EligibleTeamId)]
    public async Task UpdatePlacementAsync_ReturnsValidation_ForInvalidOutcomeTeamMatrix(
        PlacementOutcome outcome,
        long? teamId)
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(
                ClubAAssignmentId,
                outcome,
                teamId,
                _clubAConcurrencyToken),
            TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
        result.AsT1.Value.ShouldContainKey(nameof(UpdateCampaignPlacementInput.TeamId));
    }

    /// <summary>
    /// Verifies a regular club member cannot mutate placement decisions.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsync_ReturnsForbidden_WhenCallerIsNotClubAdmin()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(
                ClubAAssignmentId,
                PlacementOutcome.NotSelected,
                TeamId: null,
                _clubAConcurrencyToken),
            TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies tenant filters hide another club's participation from an administrator.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsync_ReturnsNotFound_ForCrossTenantParticipation()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(
                ClubBAssignmentId,
                PlacementOutcome.NotSelected,
                TeamId: null,
                Guid.NewGuid()),
            TestContext.Current.CancellationToken);

        result.IsT2.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies tenant filters prevent assigning a team owned by another club.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsync_ReturnsNotFound_ForCrossTenantTeam()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(
                ClubAAssignmentId,
                PlacementOutcome.Assigned,
                ClubBTeamId,
                _clubAConcurrencyToken),
            TestContext.Current.CancellationToken);

        result.IsT2.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies an ineligible team is rejected without changing participation state.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsync_ReturnsValidation_AndDoesNotWrite_WhenTeamIsIneligible()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(
                ClubAAssignmentId,
                PlacementOutcome.Assigned,
                IneligibleTeamId,
                _clubAConcurrencyToken),
            TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var participation = await verify.PlayerCampaignAssignments
            .SingleAsync(
                assignment => assignment.PlayerCampaignAssignmentId == ClubAAssignmentId,
                TestContext.Current.CancellationToken);
        participation.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        participation.TeamId.ShouldBeNull();
        participation.ConcurrencyToken.ShouldBe(_clubAConcurrencyToken);
    }

    /// <summary>
    /// Verifies a stale token returns a conflict and cannot overwrite the newer placement.
    /// </summary>
    [Fact]
    public async Task UpdatePlacementAsync_ReturnsConflict_AndDoesNotOverwrite_WhenTokenIsStale()
    {
        var newerToken = Guid.NewGuid();
        await using (var update = _harness.CreateAdminContext())
        {
            var participation = await update.PlayerCampaignAssignments
                .SingleAsync(
                    assignment => assignment.PlayerCampaignAssignmentId == ClubAAssignmentId,
                    TestContext.Current.CancellationToken);
            participation.PlacementOutcome = PlacementOutcome.Withdrawn;
            participation.ConcurrencyToken = newerToken;
            await update.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdatePlacementAsync(
            new UpdateCampaignPlacementInput(
                ClubAAssignmentId,
                PlacementOutcome.NotSelected,
                TeamId: null,
                _clubAConcurrencyToken),
            TestContext.Current.CancellationToken);

        result.IsT4.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var persisted = await verify.PlayerCampaignAssignments
            .SingleAsync(
                assignment => assignment.PlayerCampaignAssignmentId == ClubAAssignmentId,
                TestContext.Current.CancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Withdrawn);
        persisted.ConcurrencyToken.ShouldBe(newerToken);
    }

    /// <summary>
    /// Creates the placement service over the shared SQLite tenancy harness.
    /// </summary>
    /// <returns>A service instance using the mutable fake current-user provider.</returns>
    private CampaignPlacementService CreateService()
    {
        IDbContextFactory<NovaDbContext> dbContextFactory =
            new TestDbContextFactory<NovaDbContext>(() => _harness.CreateTenantContext());

        return new CampaignPlacementService(
            dbContextFactory,
            _harness.CurrentUser,
            NullLogger<CampaignPlacementService>.Instance);
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
    /// Seeds two clubs with campaign participation and placement teams.
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
                SeasonId = 500,
                Name = "Season A",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new SeasonEntity
            {
                SeasonId = 501,
                Name = "Season B",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.Campaigns.AddRange(
            new CampaignEntity
            {
                CampaignId = 600,
                Name = "Campaign A",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = 500,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CampaignId = 601,
                Name = "Campaign B",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = 501,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.Players.AddRange(
            new PlayerEntity
            {
                PlayerId = 700,
                FirstName = "Alex",
                LastName = "Able",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                PlayerId = 701,
                FirstName = "Blair",
                LastName = "Baker",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.Teams.AddRange(
            new TeamEntity
            {
                TeamId = EligibleTeamId,
                Name = "Eligible",
                GraduationYear = 2029,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                TeamId = IneligibleTeamId,
                Name = "Ineligible",
                GraduationYear = 2031,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                TeamId = ClubBTeamId,
                Name = "Club B Team",
                GraduationYear = 2029,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ClubAAssignmentId,
                PlayerId = 700,
                CampaignId = 600,
                ClubId = ClubAId,
                PlacementOutcome = PlacementOutcome.Undecided,
                ConcurrencyToken = _clubAConcurrencyToken,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ClubBAssignmentId,
                PlayerId = 701,
                CampaignId = 601,
                ClubId = ClubBId,
                PlacementOutcome = PlacementOutcome.Undecided,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedById = ClubBAdminId
            });

        db.SaveChanges();
    }
}
