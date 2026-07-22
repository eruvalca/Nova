using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Campaigns;
using Nova.Shared.Enums;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests campaign tag application authorization, lifecycle guards, tenant isolation, and uniqueness.
/// </summary>
public sealed class CampaignTagApplicationServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAAdminId = 200;
    private const long ClubAMemberId = 201;
    private const long ClubAOtherMemberId = 202;
    private const long ClubBMemberId = 203;
    private const long ActiveAssignmentId = 300;
    private const long ClosedAssignmentId = 301;
    private const long ClubBAssignmentId = 302;
    private const long ActiveTagId = 400;
    private const long SecondaryActiveTagId = 403;
    private const long ArchivedTagId = 401;
    private const long ClubBTagId = 402;
    private const long ExistingApplicationId = 500;
    private const long ClosedCampaignApplicationId = 501;
    private const long ArchivedTagApplicationId = 502;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes seeded campaign tag application data for two clubs.
    /// </summary>
    public CampaignTagApplicationServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies any club member can apply an active tag definition to active-campaign participation.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_CreatesApplication_ForClubMember_InActiveCampaign()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { PlayerCampaignAssignmentId = ActiveAssignmentId, PlayerTagId = SecondaryActiveTagId },
            TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var created = await verify.CampaignTagApplications
            .SingleAsync(candidate => candidate.CampaignTagApplicationId == result.AsT0.CampaignTagApplicationId, TestContext.Current.CancellationToken);
        created.PlayerCampaignAssignmentId.ShouldBe(ActiveAssignmentId);
        created.PlayerTagId.ShouldBe(SecondaryActiveTagId);
        created.ClubId.ShouldBe(ClubAId);
        created.CreatedById.ShouldBe(ClubAMemberId);
    }

    /// <summary>
    /// Verifies duplicate participation/tag applications are rejected.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ReturnsConflict_ForDuplicateParticipationTagPair()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { PlayerCampaignAssignmentId = ActiveAssignmentId, PlayerTagId = ActiveTagId },
            TestContext.Current.CancellationToken);

        result.IsT4.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies archived tag definitions cannot be applied.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ReturnsConflict_ForArchivedTagDefinition()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { PlayerCampaignAssignmentId = ActiveAssignmentId, PlayerTagId = ArchivedTagId },
            TestContext.Current.CancellationToken);

        result.IsT4.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies closed campaigns reject new tag applications.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ReturnsConflict_ForClosedCampaign()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { PlayerCampaignAssignmentId = ClosedAssignmentId, PlayerTagId = ActiveTagId },
            TestContext.Current.CancellationToken);

        result.IsT4.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies tenant filters hide other-club participation from apply operations.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ReturnsNotFound_ForCrossTenantParticipation()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { PlayerCampaignAssignmentId = ClubBAssignmentId, PlayerTagId = ActiveTagId },
            TestContext.Current.CancellationToken);

        result.IsT2.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies tenant filters hide other-club tags from apply operations.
    /// </summary>
    [Fact]
    public async Task ApplyAsync_ReturnsNotFound_ForCrossTenantTagDefinition()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { PlayerCampaignAssignmentId = ActiveAssignmentId, PlayerTagId = ClubBTagId },
            TestContext.Current.CancellationToken);

        result.IsT2.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies the creating user can remove their own application.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_RemovesApplication_ForApplyingUser()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.RemoveAsync(
            new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = ExistingApplicationId },
            TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        (await verify.CampaignTagApplications
            .AnyAsync(candidate => candidate.CampaignTagApplicationId == ExistingApplicationId, TestContext.Current.CancellationToken))
            .ShouldBeFalse();
    }

    /// <summary>
    /// Verifies club administrators can remove applications created by other members.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_RemovesApplication_ForClubAdministrator()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.RemoveAsync(
            new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = ExistingApplicationId },
            TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies non-owner non-admin users cannot remove applications.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_ReturnsForbidden_ForNonOwnerNonAdmin()
    {
        ActAs(ClubAOtherMemberId, ClubAId);
        var service = CreateService();

        var result = await service.RemoveAsync(
            new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = ExistingApplicationId },
            TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies closed campaigns are read-only for tag application removals.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_ReturnsConflict_ForClosedCampaign()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.RemoveAsync(
            new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = ClosedCampaignApplicationId },
            TestContext.Current.CancellationToken);

        result.IsT4.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies archived tag definitions block removals to preserve archived history.
    /// </summary>
    [Fact]
    public async Task RemoveAsync_ReturnsConflict_ForArchivedTagDefinition()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.RemoveAsync(
            new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = ArchivedTagApplicationId },
            TestContext.Current.CancellationToken);

        result.IsT4.ShouldBeTrue();
    }

    /// <summary>
    /// Creates the campaign tag application service over the shared SQLite tenancy harness.
    /// </summary>
    /// <returns>A service instance using the mutable fake current-user provider.</returns>
    private CampaignTagApplicationService CreateService()
    {
        IDbContextFactory<NovaDbContext> dbContextFactory =
            new TestDbContextFactory<NovaDbContext>(() => _harness.CreateTenantContext());

        return new CampaignTagApplicationService(
            dbContextFactory,
            _harness.CurrentUser,
            NullLogger<CampaignTagApplicationService>.Instance);
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
    /// Seeds campaigns, participation rows, tag definitions, and applications across two clubs.
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
                CreatedById = ClubBMemberId
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
                CreatedById = ClubBMemberId
            });

        db.Campaigns.AddRange(
            new CampaignEntity
            {
                CampaignId = 700,
                Name = "Active Campaign A",
                SeasonId = 600,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CampaignId = 701,
                Name = "Closed Campaign A",
                SeasonId = 600,
                ClubId = ClubAId,
                Status = CampaignStatus.Closed,
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ClosedById = ClubAAdminId,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CampaignId = 702,
                Name = "Active Campaign B",
                SeasonId = 601,
                ClubId = ClubBId,
                CreatedById = ClubBMemberId
            });

        db.Players.AddRange(
            new PlayerEntity
            {
                PlayerId = 800,
                FirstName = "A",
                LastName = "Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                PlayerId = 801,
                FirstName = "B",
                LastName = "Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubBId,
                CreatedById = ClubBMemberId
            });

        db.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                PlayerId = 800,
                CampaignId = 700,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ClosedAssignmentId,
                PlayerId = 800,
                CampaignId = 701,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ClubBAssignmentId,
                PlayerId = 801,
                CampaignId = 702,
                ClubId = ClubBId,
                CreatedById = ClubBMemberId
            });

        db.PlayerTags.AddRange(
            new PlayerTagEntity
            {
                PlayerTagId = ActiveTagId,
                Name = "Active",
                Color = "#00FF00",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                PlayerTagId = SecondaryActiveTagId,
                Name = "Secondary Active",
                Color = "#00AAFF",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                PlayerTagId = ArchivedTagId,
                Name = "Archived",
                Color = "#FF0000",
                ClubId = ClubAId,
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = DateTimeOffset.UtcNow.AddDays(-2),
                ArchivedById = ClubAAdminId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                PlayerTagId = ClubBTagId,
                Name = "B Tag",
                Color = "#0000FF",
                ClubId = ClubBId,
                CreatedById = ClubBMemberId
            });

        db.CampaignTagApplications.AddRange(
            new CampaignTagApplicationEntity
            {
                CampaignTagApplicationId = ExistingApplicationId,
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                PlayerTagId = ActiveTagId,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            },
            new CampaignTagApplicationEntity
            {
                CampaignTagApplicationId = ClosedCampaignApplicationId,
                PlayerCampaignAssignmentId = ClosedAssignmentId,
                PlayerTagId = ActiveTagId,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            },
            new CampaignTagApplicationEntity
            {
                CampaignTagApplicationId = ArchivedTagApplicationId,
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                PlayerTagId = ArchivedTagId,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            });

        db.SaveChanges();
    }
}
