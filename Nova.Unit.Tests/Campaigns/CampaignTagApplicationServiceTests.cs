using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests campaign tag application authorization, lifecycle guards, tenant isolation, and uniqueness.
/// </summary>
public sealed partial class CampaignTagApplicationServiceTests : IDisposable
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
    public async Task ApplyAsyncCreatesApplicationForClubMemberInActiveCampaignAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ActiveAssignmentId, PlayerTagId = SecondaryActiveTagId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var created = await verify.CampaignTagApplications
            .SingleAsync(candidate => candidate.CampaignTagApplicationId == result.Value.CampaignTagApplicationId, TestContext.Current.CancellationToken);
        created.PlayerCampaignAssignmentId.ShouldBe(ActiveAssignmentId);
        created.PlayerTagId.ShouldBe(SecondaryActiveTagId);
        created.ClubId.ShouldBe(ClubAId);
        created.CreatedById.ShouldBe(ClubAMemberId);
    }

    /// <summary>
    /// Verifies duplicate participation/tag applications are rejected.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReportsAlreadyAppliedWithoutChangingOriginalActorAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ActiveAssignmentId, PlayerTagId = ActiveTagId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AlreadyApplied.ShouldBeTrue();
        result.Value.CampaignTagApplicationId.ShouldBe(ExistingApplicationId);
        using var verify = _harness.CreateAdminContext();
        (await verify.CampaignTagApplications.SingleAsync(application => application.CampaignTagApplicationId == ExistingApplicationId, TestContext.Current.CancellationToken)).CreatedById.ShouldBe(ClubAMemberId);
    }

    /// <summary>
    /// Verifies Draft campaigns reject tag applications without persisting an application or side effect.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsConflictWithoutWritesOrActivityForDraftCampaignAsync()
    {
        await MakeCampaignDraftAsync(ActiveAssignmentId);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput
            {
                OperationId = Guid.CreateVersion7(),
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                PlayerTagId = SecondaryActiveTagId
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("This campaign is read-only. Refresh to see its current status; keep or copy your draft.");

        await using var verify = _harness.CreateAdminContext();
        (await verify.CampaignTagApplications.AnyAsync(
            application => application.PlayerCampaignAssignmentId == ActiveAssignmentId
                && application.PlayerTagId == SecondaryActiveTagId,
            TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.EvaluationMutationReceipts.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.ActivityEvents.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    /// <summary>
    /// Verifies archived tag definitions cannot be applied.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsConflictForArchivedTagDefinitionAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ActiveAssignmentId, PlayerTagId = ArchivedTagId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>
    /// Verifies closed campaigns reject new tag applications.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsConflictForClosedCampaignAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ClosedAssignmentId, PlayerTagId = ActiveTagId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("This campaign is read-only. Refresh to see its current status; keep or copy your draft.");
    }

    /// <summary>
    /// Verifies tenant filters hide other-club participation from apply operations.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsNotFoundForCrossTenantParticipationAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ClubBAssignmentId, PlayerTagId = ActiveTagId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies tenant filters hide other-club tags from apply operations.
    /// </summary>
    [Fact]
    public async Task ApplyAsyncReturnsNotFoundForCrossTenantTagDefinitionAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ApplyAsync(
            new ApplyCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = ActiveAssignmentId, PlayerTagId = ClubBTagId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies the creating user can remove their own application.
    /// </summary>
    [Fact]
    public async Task RemoveAsyncRemovesApplicationForApplyingUserAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.RemoveAsync(
            new RemoveCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), CampaignTagApplicationId = ExistingApplicationId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        (await verify.CampaignTagApplications
            .AnyAsync(candidate => candidate.CampaignTagApplicationId == ExistingApplicationId, TestContext.Current.CancellationToken))
            .ShouldBeFalse();
    }

    /// <summary>
    /// Verifies club administrators can remove applications created by other members.
    /// </summary>
    [Fact]
    public async Task RemoveAsyncRemovesApplicationForClubAdministratorAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.RemoveAsync(
            new RemoveCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), CampaignTagApplicationId = ExistingApplicationId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies non-owner non-admin users cannot remove applications.
    /// </summary>
    [Fact]
    public async Task RemoveAsyncReturnsForbiddenForNonOwnerNonAdminAsync()
    {
        ActAs(ClubAOtherMemberId, ClubAId);
        var service = CreateService();

        var result = await service.RemoveAsync(
            new RemoveCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), CampaignTagApplicationId = ExistingApplicationId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies closed campaigns are read-only for tag application removals.
    /// </summary>
    [Fact]
    public async Task RemoveAsyncReturnsConflictForClosedCampaignAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.RemoveAsync(
            new RemoveCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), CampaignTagApplicationId = ClosedCampaignApplicationId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("This campaign is read-only. Refresh to see its current status; keep or copy your draft.");
    }

    /// <summary>
    /// Verifies Draft campaigns reject tag removal without deleting the application or recording side effects.
    /// </summary>
    [Fact]
    public async Task RemoveAsyncReturnsConflictWithoutWritesOrActivityForDraftCampaignAsync()
    {
        await MakeCampaignDraftAsync(ActiveAssignmentId);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.RemoveAsync(
            new RemoveCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), CampaignTagApplicationId = ExistingApplicationId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("This campaign is read-only. Refresh to see its current status; keep or copy your draft.");

        await using var verify = _harness.CreateAdminContext();
        (await verify.CampaignTagApplications.AnyAsync(
            application => application.CampaignTagApplicationId == ExistingApplicationId,
            TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await verify.EvaluationMutationReceipts.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.ActivityEvents.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    /// <summary>
    /// Verifies archived tag definitions block removals to preserve archived history.
    /// </summary>
    [Fact]
    public async Task RemoveAsyncReturnsConflictForArchivedTagDefinitionAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.RemoveAsync(
            new RemoveCampaignTagApplicationInput { OperationId = Guid.CreateVersion7(), CampaignTagApplicationId = ArchivedTagApplicationId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
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
    /// Changes the campaign for an existing assignment from Active to Draft for mutation rejection tests.
    /// </summary>
    /// <param name="assignmentId">The assignment whose campaign should become Draft.</param>
    private async Task MakeCampaignDraftAsync(long assignmentId)
    {
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using var db = _harness.CreateAdminContext();
#pragma warning restore MA0004
        var campaign = await db.PlayerCampaignAssignments
            .Where(assignment => assignment.PlayerCampaignAssignmentId == assignmentId)
            .Select(assignment => assignment.Campaign)
            .SingleAsync(TestContext.Current.CancellationToken);
        campaign.Status = CampaignStatus.Draft;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
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
#pragma warning disable MA0051 // Keep the complete arrangement, operation, and assertions together as one regression scenario.
    private void Seed()
#pragma warning restore MA0051
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
                CreatedById = ClubBMemberId
            });

        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAAdminId, ClubId = ClubAId, FirstName = "Admin", LastName = "A" },
            new NovaUserEntity { Id = ClubAMemberId, ClubId = ClubAId, FirstName = "Member", LastName = "A" },
            new NovaUserEntity { Id = ClubAOtherMemberId, ClubId = ClubAId, FirstName = "Other", LastName = "A" },
            new NovaUserEntity { Id = ClubBMemberId, ClubId = ClubBId, FirstName = "Member", LastName = "B" });
        db.Roles.Add(new IdentityRole<long> { Id = 900, Name = Roles.ClubAdmin, NormalizedName = Roles.ClubAdmin.ToUpperInvariant() });
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = ClubAAdminId, RoleId = 900 });

        db.Seasons.AddRange(
            new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                SeasonId = 600,
                Name = "Season A",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                SeasonId = 601,
                Name = "Season B",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubBId,
                CreatedById = ClubBMemberId
            });

        db.Campaigns.AddRange(
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = 700,
                Name = "Active Campaign A",
                SeasonId = 600,
                ClubId = ClubAId,
                Status = CampaignStatus.Active,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
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
                CreationOperationId = Guid.NewGuid(),
                CampaignId = 702,
                Name = "Active Campaign B",
                SeasonId = 601,
                ClubId = ClubBId,
                Status = CampaignStatus.Active,
                CreatedById = ClubBMemberId
            });

        db.Players.AddRange(
            new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
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
                CreationOperationId = Guid.NewGuid(),
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
                CreationOperationId = Guid.NewGuid(),
                PlayerTagId = ActiveTagId,
                Name = "Active",
                NormalizedName = "ACTIVE",
                Color = "#00FF00",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerTagId = SecondaryActiveTagId,
                Name = "Secondary Active",
                NormalizedName = "SECONDARY ACTIVE",
                Color = "#00AAFF",
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerTagId = ArchivedTagId,
                Name = "Archived",
                NormalizedName = "ARCHIVED",
                Color = "#FF0000",
                ClubId = ClubAId,
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = DateTimeOffset.UtcNow.AddDays(-2),
                ArchivedById = ClubAAdminId,
                CreatedById = ClubAAdminId
            },
            new PlayerTagEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerTagId = ClubBTagId,
                Name = "B Tag",
                NormalizedName = "B TAG",
                Color = "#0000FF",
                ClubId = ClubBId,
                CreatedById = ClubBMemberId
            });

        db.CampaignTagApplications.AddRange(
            new CampaignTagApplicationEntity
            {
                AuthorDisplayName = "Member A",
                CreationOperationId = Guid.NewGuid(),
                CampaignTagApplicationId = ExistingApplicationId,
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                PlayerTagId = ActiveTagId,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            },
            new CampaignTagApplicationEntity
            {
                AuthorDisplayName = "Member A",
                CreationOperationId = Guid.NewGuid(),
                CampaignTagApplicationId = ClosedCampaignApplicationId,
                PlayerCampaignAssignmentId = ClosedAssignmentId,
                PlayerTagId = ActiveTagId,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            },
            new CampaignTagApplicationEntity
            {
                AuthorDisplayName = "Member A",
                CreationOperationId = Guid.NewGuid(),
                CampaignTagApplicationId = ArchivedTagApplicationId,
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                PlayerTagId = ArchivedTagId,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            });

        db.SaveChanges();
    }
}
