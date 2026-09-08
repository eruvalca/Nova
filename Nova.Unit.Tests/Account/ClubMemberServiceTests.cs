using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Components.Account;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Account;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Account;
using Nova.SharedKernel.Features.Activity;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Account;

public sealed class ClubMemberServiceTests : IDisposable
{
    private const long ClubId = 200;
    private const long OtherClubId = 201;
    private const long AdminId = 300;
    private const long SecondAdminId = 301;
    private const long MemberId = 302;
    private const long OtherClubMemberId = 303;

    private readonly TenancyTestHarness _harness = new();
    private readonly UserManager<NovaUserEntity> _userManager;
    private readonly SignInManager<NovaUserEntity> _signInManager;

    public ClubMemberServiceTests()
    {
        (_userManager, _signInManager) = CreateIdentityManagers();
        Seed();
        _harness.CurrentUser.UserId = AdminId;
        _harness.CurrentUser.ClubId = ClubId;
        _harness.CurrentUser.IsClubAdmin = true;
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetClubMembersAsyncReturnsOtherMembersOnlyAsync()
    {
        var result = await CreateService().GetClubMembersAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(member => member.UserId).ShouldBe([SecondAdminId, MemberId], ignoreOrder: true);
    }

    [Fact]
    public async Task PromoteMemberAsyncPersistsRoleStampAndActivityAtomicallyAsync()
    {
        string? oldStamp;
        using (var before = _harness.CreateAdminContext())
        {
            oldStamp = (await before.Users.SingleAsync(user => user.Id == MemberId, TestContext.Current.CancellationToken)).SecurityStamp;
        }

        var result = await CreateService().PromoteMemberAsync(MemberInput(MemberId), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var db = _harness.CreateAdminContext();
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
        var roleId = (await db.Roles.SingleAsync(role => role.NormalizedName == Roles.ClubAdmin.ToUpperInvariant(), TestContext.Current.CancellationToken)).Id;
#pragma warning restore CA1862
        db.UserRoles.ShouldContain(role => role.UserId == MemberId && role.RoleId == roleId);
        (await db.Users.SingleAsync(user => user.Id == MemberId, TestContext.Current.CancellationToken)).SecurityStamp.ShouldNotBe(oldStamp, StringComparer.Ordinal);
        (await db.ClubMembershipMutationReceipts.CountAsync(receipt => receipt.MemberUserId == MemberId, TestContext.Current.CancellationToken)).ShouldBe(1);
        var activity = (await db.ActivityEvents.SingleAsync(activity => activity.EventKind == ActivityEventKind.MemberPromoted, TestContext.Current.CancellationToken));
        var context = JsonSerializer.Deserialize<MemberRoleContext>(activity.PayloadJson, JsonOptions());
        context!.MemberUserId.ShouldBe(MemberId);
        context.MemberDisplayName.ShouldBe("Member One");
    }

    [Fact]
    public async Task PromoteMemberAsyncReturnsValidationForInvalidMemberIdAsync()
    {
        var result = await CreateService().PromoteMemberAsync(MemberInput(0), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull()
            .ShouldContainKey(nameof(ClubMemberMutationInput.MemberUserId));
    }

    [Fact]
    public async Task PromoteMemberAsyncIsIdempotentWithoutAnotherEventOrStampChangeAsync()
    {
        await CreateService().PromoteMemberAsync(MemberInput(MemberId), TestContext.Current.CancellationToken);
        string? stamp;
        using (var db = _harness.CreateAdminContext())
        {
            stamp = (await db.Users.SingleAsync(user => user.Id == MemberId, TestContext.Current.CancellationToken)).SecurityStamp;
        }

        var result = await CreateService().PromoteMemberAsync(MemberInput(MemberId), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var after = _harness.CreateAdminContext();
        (await after.Users.SingleAsync(user => user.Id == MemberId, TestContext.Current.CancellationToken)).SecurityStamp.ShouldBe(stamp);
        (await after.ActivityEvents.CountAsync(activity => activity.EventKind == ActivityEventKind.MemberPromoted, TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>Verifies one membership mutation prunes expired receipts from every club scope.</summary>
    [Fact]
    public async Task PromoteMemberAsyncPrunesExpiredReceiptsGloballyAsync()
    {
        var currentClubReceiptOperationId = Guid.NewGuid();
        var otherClubReceiptOperationId = Guid.NewGuid();
        using (var setup = _harness.CreateAdminContext())
        {
            var expiredAt = DateTimeOffset.UtcNow.AddDays(-2);
            setup.ClubMembershipMutationReceipts.AddRange(
                new ClubMembershipMutationReceiptEntity
                {
                    OperationId = currentClubReceiptOperationId,
                    MemberUserId = AdminId,
                    MutationKind = "Promote",
                    ClubId = ClubId,
                    CreatedAt = expiredAt,
                    CreatedById = AdminId,
                },
                new ClubMembershipMutationReceiptEntity
                {
                    OperationId = otherClubReceiptOperationId,
                    MemberUserId = OtherClubMemberId,
                    MutationKind = "Promote",
                    ClubId = OtherClubId,
                    CreatedAt = expiredAt,
                    CreatedById = OtherClubMemberId,
                });
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);

            foreach (var receipt in setup.ClubMembershipMutationReceipts.Local)
            {
                receipt.CreatedAt = expiredAt;
            }

            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().PromoteMemberAsync(
            MemberInput(MemberId),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var verify = _harness.CreateAdminContext();
        verify.ClubMembershipMutationReceipts.ShouldNotContain(
            receipt => receipt.OperationId == currentClubReceiptOperationId);
        verify.ClubMembershipMutationReceipts.ShouldNotContain(
            receipt => receipt.OperationId == otherClubReceiptOperationId);
    }

    [Fact]
    public async Task DemoteMemberAsyncRejectsSoleAdministratorAsync()
    {
        using (var db = _harness.CreateAdminContext())
        {
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            var roleId = (await db.Roles.SingleAsync(role => role.NormalizedName == Roles.ClubAdmin.ToUpperInvariant(), TestContext.Current.CancellationToken)).Id;
#pragma warning restore CA1862
            db.UserRoles.Remove((await db.UserRoles.SingleAsync(role => role.UserId == SecondAdminId && role.RoleId == roleId, TestContext.Current.CancellationToken)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().DemoteMemberAsync(MemberInput(AdminId), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task DemoteMemberAsyncAllowsSelfDemotionAndRefreshesSignInAsync()
    {
        var result = await CreateService().DemoteMemberAsync(MemberInput(AdminId), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var db = _harness.CreateAdminContext();
        db.UserRoles.ShouldNotContain(role => role.UserId == AdminId);
        (await db.ActivityEvents.CountAsync(activity => activity.EventKind == ActivityEventKind.MemberDemoted, TestContext.Current.CancellationToken)).ShouldBe(1);
        await _signInManager.Received(1).RefreshSignInAsync(Arg.Is<NovaUserEntity>(user => user.Id == AdminId));
    }

    [Fact]
    public async Task DemoteMemberAsyncIdempotentSelfRetryRefreshesSignInWithoutDuplicateEventAsync()
    {
        var service = CreateService();
        await service.DemoteMemberAsync(MemberInput(AdminId), TestContext.Current.CancellationToken);

        var retry = await service.DemoteMemberAsync(MemberInput(AdminId), TestContext.Current.CancellationToken);

        retry.IsSuccess.ShouldBeTrue();
        using var db = _harness.CreateAdminContext();
        (await db.ActivityEvents.CountAsync(activity => activity.EventKind == ActivityEventKind.MemberDemoted, TestContext.Current.CancellationToken)).ShouldBe(1);
        await _signInManager.Received(2).RefreshSignInAsync(Arg.Is<NovaUserEntity>(user => user.Id == AdminId));
    }

    [Fact]
    public async Task RemoveMemberAsyncClearsMembershipAndWritesOnlyRemovedEventAsync()
    {
        using (var setup = _harness.CreateAdminContext())
        {
            setup.ClubJoinRequests.Add(new ClubJoinRequestEntity
            {
                ClubId = ClubId,
                RequestingUserId = SecondAdminId,
                Status = RequestStatus.Approved,
                CreatedById = SecondAdminId,
            });
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().RemoveMemberAsync(MemberInput(SecondAdminId), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var db = _harness.CreateAdminContext();
        (await db.Users.SingleAsync(user => user.Id == SecondAdminId, TestContext.Current.CancellationToken)).ClubId.ShouldBeNull();
        db.UserRoles.ShouldNotContain(role => role.UserId == SecondAdminId);
        db.ClubJoinRequests.ShouldNotContain(request => request.RequestingUserId == SecondAdminId);
        (await db.ActivityEvents.CountAsync(activity => activity.EventKind == ActivityEventKind.MemberRemoved, TestContext.Current.CancellationToken)).ShouldBe(1);
        db.ActivityEvents.ShouldNotContain(activity => activity.EventKind == ActivityEventKind.MemberDemoted);
    }

    [Fact]
    public async Task RemoveMemberAsyncRotatesConcurrencyStampAndRejectsStaleIdentityWriteAsync()
    {
        using var staleDb = _harness.CreateAdminContext();
        var staleMember = (await staleDb.Users.SingleAsync(user => user.Id == MemberId, TestContext.Current.CancellationToken));
        var originalConcurrencyStamp = staleMember.ConcurrencyStamp;

        var result = await CreateService().RemoveMemberAsync(
            MemberInput(MemberId),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using (var verify = _harness.CreateAdminContext())
        {
            (await verify.Users.SingleAsync(user => user.Id == MemberId, TestContext.Current.CancellationToken)).ConcurrencyStamp
                .ShouldNotBe(originalConcurrencyStamp, StringComparer.Ordinal);
        }

        staleMember.SecurityStamp = "stale-identity-write";
        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => staleDb.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveMemberAsyncPreservesResolvedJoinRequestOwnedByAnotherClubAsync()
    {
        long joinRequestId;
        using (var setup = _harness.CreateAdminContext())
        {
            var request = new ClubJoinRequestEntity
            {
                ClubId = OtherClubId,
                RequestingUserId = MemberId,
                Status = RequestStatus.Rejected,
                CreatedById = MemberId,
            };
            setup.ClubJoinRequests.Add(request);
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
            joinRequestId = request.ClubJoinRequestId;
        }

        var result = await CreateService().RemoveMemberAsync(
            MemberInput(MemberId),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var verify = _harness.CreateAdminContext();
        verify.ClubJoinRequests.ShouldContain(request => request.ClubJoinRequestId == joinRequestId);
    }

    [Fact]
    public async Task RemoveMemberAsyncUsesNonDisclosingNotFoundForCrossClubTargetAsync()
    {
        var result = await CreateService().RemoveMemberAsync(MemberInput(OtherClubMemberId), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task RemoveMemberAsyncRejectsSelfRemovalAsync()
    {
        var result = await CreateService().RemoveMemberAsync(MemberInput(AdminId), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldNotBeNull();
        result.Problem.Detail.ShouldContain("leave-club");
    }

    [Fact]
    public async Task LeaveClubAsyncRejectsFinalMemberWithDeletionGuidanceAsync()
    {
        using (var db = _harness.CreateAdminContext())
        {
            (await db.Users.Where(user => user.ClubId == ClubId && user.Id != AdminId).ToListAsync(TestContext.Current.CancellationToken))
                .ForEach(user => user.ClubId = null);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().LeaveClubAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("The final club member cannot leave. Delete the club instead.");
    }

    [Fact]
    public async Task LeaveClubAsyncClearsMembershipWritesLeftEventAndRefreshesSignInAsync()
    {
        _harness.CurrentUser.UserId = MemberId;
        _harness.CurrentUser.IsClubAdmin = false;
        using (var setup = _harness.CreateAdminContext())
        {
            setup.ClubJoinRequests.Add(new ClubJoinRequestEntity
            {
                ClubId = ClubId,
                RequestingUserId = MemberId,
                Status = RequestStatus.Approved,
                CreatedById = MemberId,
            });
            await setup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().LeaveClubAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var db = _harness.CreateAdminContext();
        var departedMember = (await db.Users.SingleAsync(user => user.Id == MemberId, TestContext.Current.CancellationToken));
        departedMember.ClubId.ShouldBeNull();
        db.ClubJoinRequests.ShouldNotContain(request => request.RequestingUserId == MemberId);
        (await db.ActivityEvents.CountAsync(activity => activity.EventKind == ActivityEventKind.MemberLeft, TestContext.Current.CancellationToken)).ShouldBe(1);
        await _signInManager.Received(1).RefreshSignInAsync(Arg.Is<NovaUserEntity>(user =>
            user.Id == MemberId
            && user.ClubId == null
            && user.SecurityStamp == departedMember.SecurityStamp));
    }

    [Fact]
    public async Task LeaveClubAsyncIsIdempotentForStaleMemberCookieAsync()
    {
        _harness.CurrentUser.UserId = MemberId;
        _harness.CurrentUser.IsClubAdmin = false;
        var service = CreateService();
        await service.LeaveClubAsync(TestContext.Current.CancellationToken);

        var retry = await service.LeaveClubAsync(TestContext.Current.CancellationToken);

        retry.IsSuccess.ShouldBeTrue();
        using var db = _harness.CreateAdminContext();
        (await db.ActivityEvents.CountAsync(activity => activity.EventKind == ActivityEventKind.MemberLeft, TestContext.Current.CancellationToken)).ShouldBe(1);
        await _signInManager.Received(2).RefreshSignInAsync(Arg.Is<NovaUserEntity>(user => user.Id == MemberId));
    }

    [Fact]
    public async Task LeaveClubAsyncRejectsSoleAdministratorWhenOtherMembersRemainAsync()
    {
        using (var db = _harness.CreateAdminContext())
        {
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            var roleId = (await db.Roles.SingleAsync(role => role.NormalizedName == Roles.ClubAdmin.ToUpperInvariant(), TestContext.Current.CancellationToken)).Id;
#pragma warning restore CA1862
            db.UserRoles.Remove((await db.UserRoles.SingleAsync(role => role.UserId == SecondAdminId && role.RoleId == roleId, TestContext.Current.CancellationToken)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().LeaveClubAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>Creates a valid member-mutation input for the specified identity user.</summary>
    private static ClubMemberMutationInput MemberInput(long memberUserId) => new() { MemberUserId = memberUserId };

    private ClubMemberService CreateService()
        => new(
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            new TestDbContextFactory<NovaAdminDbContext>(_harness.CreateAdminContext),
            _harness.CurrentUser,
            new ClubMembershipClaimRefresher(_userManager, _signInManager),
            NullLogger<ClubMemberService>.Instance);

    private (UserManager<NovaUserEntity>, SignInManager<NovaUserEntity>) CreateIdentityManagers()
    {
        var store = Substitute.For<IUserStore<NovaUserEntity>>();
        var manager = Substitute.For<UserManager<NovaUserEntity>>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<NovaUserEntity>(),
            Array.Empty<IUserValidator<NovaUserEntity>>(),
            Array.Empty<IPasswordValidator<NovaUserEntity>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<NovaUserEntity>>.Instance);
        manager.FindByIdAsync(Arg.Any<string>()).Returns(call =>
        {
            using var db = _harness.CreateAdminContext();
            var id = long.Parse(call.Arg<string>(), System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult(db.Users.SingleOrDefault(user => user.Id == id));
        });

        var signIn = Substitute.For<SignInManager<NovaUserEntity>>(
            manager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<NovaUserEntity>>(),
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<NovaUserEntity>>.Instance,
            Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<NovaUserEntity>>());
        return (manager, signIn);
    }

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubId, Name = "Club A", City = "Austin", State = "TX", CreatedById = AdminId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = OtherClubId, Name = "Club B", City = "Boston", State = "MA", CreatedById = OtherClubMemberId });
        db.Users.AddRange(
            User(AdminId, "Admin", "One", ClubId),
            User(SecondAdminId, "Admin", "Two", ClubId),
            User(MemberId, "Member", "One", ClubId),
            User(OtherClubMemberId, "Other", "Member", OtherClubId));
        var adminRole = new IdentityRole<long>(Roles.ClubAdmin) { Id = 10, NormalizedName = Roles.ClubAdmin.ToUpperInvariant() };
        db.Roles.Add(adminRole);
        db.UserRoles.AddRange(
            new IdentityUserRole<long> { UserId = AdminId, RoleId = adminRole.Id },
            new IdentityUserRole<long> { UserId = SecondAdminId, RoleId = adminRole.Id });
        db.SaveChanges();
    }

    private static NovaUserEntity User(long id, string firstName, string lastName, long clubId)
        => new()
        {
            Id = id,
            UserName = $"user{id}@example.com",
            NormalizedUserName = $"USER{id}@EXAMPLE.COM",
            FirstName = firstName,
            LastName = lastName,
            ClubId = clubId,
            SecurityStamp = $"stamp-{id}",
        };

    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
}
