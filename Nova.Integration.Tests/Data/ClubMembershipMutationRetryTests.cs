using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Components.Account;
using Nova.Entities;
using Nova.Features.Account;
using Nova.Shared.Enums;
using Nova.Shared.Features.Account;
using Nova.Shared.Features.Activity;
using Nova.Shared.Security;
using NSubstitute;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>Verifies PostgreSQL retry behavior for transactional club-membership mutations.</summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class ClubMembershipMutationRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>Verifies a transient pre-commit failure retries the complete promotion atomically.</summary>
    [Fact]
    public async Task Promote_RetriesCompleteAggregate_AfterTransientSaveFailure()
    {
        var interceptor = new FailFirstSaveChangesInterceptor();

        await AssertPromotedExactlyOnceAsync(interceptor, () => interceptor.FailureCount);
    }

    /// <summary>Verifies a lost commit acknowledgement is recovered through the immutable receipt.</summary>
    [Fact]
    public async Task Promote_VerifiesCompleteAggregate_AfterAmbiguousCommitFailure()
    {
        var interceptor = new FailFirstCommittedTransactionInterceptor();

        await AssertPromotedExactlyOnceAsync(interceptor, () => interceptor.FailureCount);
    }

    /// <summary>
    /// Verifies any later membership mutation globally prunes an expired receipt whose club no
    /// longer exists, so FK-less commit proof cannot accumulate indefinitely after club deletion.
    /// </summary>
    [Fact]
    public async Task Promote_PrunesExpiredReceiptForDeletedClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(cancellationToken);
        var expiredOperationId = Guid.CreateVersion7();
        await using (var setup = fixture.CreateAdminContext())
        {
            var expiredReceipt = new ClubMembershipMutationReceiptEntity
            {
                OperationId = expiredOperationId,
                MemberUserId = seed.MemberUserId,
                MutationKind = "Promote",
                ClubId = long.MaxValue,
                CreatedById = seed.AdminUserId,
            };
            setup.ClubMembershipMutationReceipts.Add(expiredReceipt);
            await setup.SaveChangesAsync(cancellationToken);
            expiredReceipt.CreatedAt = DateTimeOffset.UtcNow.AddDays(-2);
            await setup.SaveChangesAsync(cancellationToken);
        }

        fixture.CurrentUser.UserId = seed.AdminUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var factory = new RetryingAdminDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor());
        var (userManager, signInManager) = CreateIdentityManagers();
        var service = new ClubMemberService(
            new PostgresReadContextFactory(fixture),
            factory,
            fixture.CurrentUser,
            new ClubMembershipClaimRefresher(userManager, signInManager),
            NullLogger<ClubMemberService>.Instance);

        var result = await service.PromoteMemberAsync(
            new ClubMemberMutationInput { MemberUserId = seed.MemberUserId },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await using var verify = fixture.CreateAdminContext();
        (await verify.ClubMembershipMutationReceipts.AnyAsync(
            receipt => receipt.OperationId == expiredOperationId,
            cancellationToken)).ShouldBeFalse();
    }

    /// <summary>Runs one fault-injected promotion and verifies its complete durable aggregate.</summary>
    /// <param name="interceptor">The transient failure interceptor applied to retry contexts.</param>
    /// <param name="failureCount">Returns the number of injected failures.</param>
    /// <returns>A task representing the assertion.</returns>
    private async Task AssertPromotedExactlyOnceAsync(IInterceptor interceptor, Func<int> failureCount)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(cancellationToken);
        fixture.CurrentUser.UserId = seed.AdminUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var factory = new RetryingAdminDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            interceptor);
        var (userManager, signInManager) = CreateIdentityManagers();
        var service = new ClubMemberService(
            new PostgresReadContextFactory(fixture),
            factory,
            fixture.CurrentUser,
            new ClubMembershipClaimRefresher(userManager, signInManager),
            NullLogger<ClubMemberService>.Instance);

        var result = await service.PromoteMemberAsync(
            new ClubMemberMutationInput { MemberUserId = seed.MemberUserId },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureCount().ShouldBe(1);
        await using var verify = fixture.CreateAdminContext();
        var administratorRoleId = await verify.Roles
            .Where(role => role.NormalizedName == Roles.ClubAdmin.ToUpperInvariant())
            .Select(role => role.Id)
            .SingleAsync(cancellationToken);
        (await verify.UserRoles.CountAsync(
            role => role.UserId == seed.MemberUserId && role.RoleId == administratorRoleId,
            cancellationToken)).ShouldBe(1);

        var member = await verify.Users.SingleAsync(user => user.Id == seed.MemberUserId, cancellationToken);
        member.ClubId.ShouldBe(seed.ClubId);
        member.SecurityStamp.ShouldNotBe(seed.OriginalSecurityStamp);
        member.ConcurrencyStamp.ShouldNotBe(seed.OriginalConcurrencyStamp);

        var events = await verify.ActivityEvents
            .Where(activity => activity.ClubId == seed.ClubId
                && activity.EventKind == ActivityEventKind.MemberPromoted)
            .ToListAsync(cancellationToken);
        events.Count.ShouldBe(1);
        var payload = JsonSerializer.Deserialize<MemberRoleContext>(
            events[0].PayloadJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        payload.ShouldNotBeNull();
        payload.MemberUserId.ShouldBe(seed.MemberUserId);

        var receipts = await verify.ClubMembershipMutationReceipts
            .Where(receipt => receipt.ClubId == seed.ClubId
                && receipt.MemberUserId == seed.MemberUserId
                && receipt.MutationKind == "Promote")
            .ToListAsync(cancellationToken);
        receipts.Count.ShouldBe(1);
        receipts[0].OperationId.ShouldNotBe(Guid.Empty);
    }

    /// <summary>Seeds a club administrator and regular member for one isolated promotion.</summary>
    /// <param name="cancellationToken">A token that cancels seeding.</param>
    /// <returns>The identifiers and original member stamp needed for verification.</returns>
    private async Task<Seed> SeedAsync(CancellationToken cancellationToken)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var admin = new NovaUserEntity { FirstName = "Retry", LastName = $"Admin {suffix}" };
        var member = new NovaUserEntity
        {
            FirstName = "Retry",
            LastName = $"Member {suffix}",
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };
        db.Users.AddRange(admin, member);
        await db.SaveChangesAsync(cancellationToken);

        var club = new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Membership Retry Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = admin.Id,
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);
        admin.ClubId = club.ClubId;
        member.ClubId = club.ClubId;
        var administratorRoleId = await db.Roles
            .Where(role => role.NormalizedName == Roles.ClubAdmin.ToUpperInvariant())
            .Select(role => role.Id)
            .SingleAsync(cancellationToken);
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = admin.Id, RoleId = administratorRoleId });
        await db.SaveChangesAsync(cancellationToken);

        return new Seed(club.ClubId, admin.Id, member.Id, member.SecurityStamp!, member.ConcurrencyStamp!);
    }

    /// <summary>Creates substituted Identity managers required by the service constructor.</summary>
    /// <returns>The user and sign-in managers.</returns>
    private static (UserManager<NovaUserEntity>, SignInManager<NovaUserEntity>) CreateIdentityManagers()
    {
        var userManager = Substitute.For<UserManager<NovaUserEntity>>(
            Substitute.For<IUserStore<NovaUserEntity>>(),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<NovaUserEntity>(),
            Array.Empty<IUserValidator<NovaUserEntity>>(),
            Array.Empty<IPasswordValidator<NovaUserEntity>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<NovaUserEntity>>.Instance);
        var signInManager = Substitute.For<SignInManager<NovaUserEntity>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<NovaUserEntity>>(),
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<NovaUserEntity>>.Instance,
            Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<NovaUserEntity>>());
        return (userManager, signInManager);
    }

    /// <summary>Identifies one isolated membership retry aggregate.</summary>
    /// <param name="ClubId">The club identifier.</param>
    /// <param name="AdminUserId">The acting administrator identifier.</param>
    /// <param name="MemberUserId">The promoted member identifier.</param>
    /// <param name="OriginalSecurityStamp">The member's stamp before promotion.</param>
    /// <param name="OriginalConcurrencyStamp">The member's Identity concurrency stamp before promotion.</param>
    private readonly record struct Seed(
        long ClubId,
        long AdminUserId,
        long MemberUserId,
        string OriginalSecurityStamp,
        string OriginalConcurrencyStamp);
}
