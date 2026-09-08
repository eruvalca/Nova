using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Entities;
using Nova.Features.Account;
using Nova.SharedKernel.Security;
using NSubstitute;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>Verifies account deletion shares the serialized club-membership invariant.</summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class AccountDeletionMembershipRaceTests(NovaAppHostFixture fixture)
{
    /// <summary>Verifies account deletion retries atomically after a pre-commit transient failure.</summary>
    [Fact]
    public async Task DeleteAccountRetriesAfterTransientSaveFailureAsync()
    {
        var interceptor = new FailFirstSaveChangesInterceptor();

        await AssertDeletionRecoversAsync(interceptor, () => interceptor.FailureCount);
    }

    /// <summary>Verifies account deletion recovers success after a lost commit acknowledgement.</summary>
    [Fact]
    public async Task DeleteAccountVerifiesSuccessAfterAmbiguousCommitFailureAsync()
    {
        var interceptor = new FailFirstCommittedTransactionInterceptor();

        await AssertDeletionRecoversAsync(interceptor, () => interceptor.FailureCount);
    }

    /// <summary>
    /// Verifies deletion re-reads administrator counts after waiting for the club-membership lock
    /// and refuses to orphan members when a competing demotion leaves the actor as sole admin.
    /// </summary>
    [Fact]
    public async Task DeleteAccountRejectsSoleAdministratorWhenDemotionCommitsWhileWaitingAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(cancellationToken);
        fixture.CurrentUser.UserId = seed.DeletingAdminUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;
        using var userManager = CreateUserManager();
        using var deletionUserManager = CreateUserManager();
        var service = new AccountDeletionService(
            new PostgresAdminContextFactory(fixture),
            new PostgresReadContextFactory(fixture),
            userManager,
            fixture.CurrentUser,
            NullLogger<AccountDeletionService>.Instance);

        var lockKey = (long.MinValue / 32) + seed.ClubId;
        await using var holdDb = fixture.CreateAdminContext();
        await using var holdTransaction = await holdDb.Database.BeginTransactionAsync(cancellationToken);
        await holdDb.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockKey})",
            cancellationToken);

        var deleteTask = service.DeleteAccountAsync(cancellationToken);
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            holdDb,
            lockKey,
            cancellationToken);
        var administratorRoleId = await holdDb.Roles
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            .Where(role => role.NormalizedName == Roles.ClubAdmin.ToUpperInvariant())
#pragma warning restore CA1862
            .Select(role => role.Id)
            .SingleAsync(cancellationToken);
        var competingAdminRole = await holdDb.UserRoles.SingleAsync(
            role => role.UserId == seed.CompetingAdminUserId && role.RoleId == administratorRoleId,
            cancellationToken);
        holdDb.UserRoles.Remove(competingAdminRole);
        await holdDb.SaveChangesAsync(cancellationToken);
        await holdTransaction.CommitAsync(cancellationToken);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => deleteTask);

        exception.Message.ShouldContain("administrator");
        await using var verify = fixture.CreateAdminContext();
        (await verify.Users.AnyAsync(user => user.Id == seed.DeletingAdminUserId, cancellationToken)).ShouldBeTrue();
        (await verify.Clubs.AnyAsync(club => club.ClubId == seed.ClubId, cancellationToken)).ShouldBeTrue();
        (await verify.UserRoles.CountAsync(
            role => role.RoleId == administratorRoleId
                && (role.UserId == seed.DeletingAdminUserId || role.UserId == seed.CompetingAdminUserId),
            cancellationToken)).ShouldBe(1);
    }

    /// <summary>Runs one fault-injected deletion and verifies the account is removed once.</summary>
    /// <param name="interceptor">The transient failure interceptor.</param>
    /// <param name="failureCount">Returns the number of injected failures.</param>
    /// <returns>A task representing the assertion.</returns>
    private async Task AssertDeletionRecoversAsync(IInterceptor interceptor, Func<int> failureCount)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;
        long userId;
        var seed = fixture.CreateAdminContext();
        await using (seed)
        {
            var user = new NovaUserEntity
            {
                FirstName = "Deletion",
                LastName = $"Retry {Guid.NewGuid():N}",
            };
            seed.Users.Add(user);
            await seed.SaveChangesAsync(cancellationToken);
            userId = user.Id;
        }

        fixture.CurrentUser.UserId = userId;
        var factory = new RetryingAdminDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            interceptor);
        using var deletionUserManager = CreateUserManager();
        var service = new AccountDeletionService(
            factory,
            new PostgresReadContextFactory(fixture),
            deletionUserManager,
            fixture.CurrentUser,
            NullLogger<AccountDeletionService>.Instance);

        await service.DeleteAccountAsync(cancellationToken);

        failureCount().ShouldBe(1);
        var verify = fixture.CreateAdminContext();
        await using (verify)
        {
            (await verify.Users.CountAsync(user => user.Id == userId, cancellationToken)).ShouldBe(0);
        }
    }

    /// <summary>Seeds a club with two administrators for the serialized deletion race.</summary>
    /// <param name="cancellationToken">A token that cancels seeding.</param>
    /// <returns>The club and administrator identifiers.</returns>
    private async Task<Seed> SeedAsync(CancellationToken cancellationToken)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;
        var db = fixture.CreateAdminContext();
        await using (db)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var deletingAdmin = new NovaUserEntity { FirstName = "Deleting", LastName = $"Admin {suffix}" };
            var competingAdmin = new NovaUserEntity { FirstName = "Competing", LastName = $"Admin {suffix}" };
            db.Users.AddRange(deletingAdmin, competingAdmin);
            await db.SaveChangesAsync(cancellationToken);

            var club = new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Account Deletion Race Club {suffix}",
                City = "Austin",
                State = "TX",
                CreatedById = deletingAdmin.Id,
            };
            db.Clubs.Add(club);
            await db.SaveChangesAsync(cancellationToken);
            deletingAdmin.ClubId = club.ClubId;
            competingAdmin.ClubId = club.ClubId;
            var administratorRoleId = await db.Roles
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
                .Where(role => role.NormalizedName == Roles.ClubAdmin.ToUpperInvariant())
#pragma warning restore CA1862
                .Select(role => role.Id)
                .SingleAsync(cancellationToken);
            db.UserRoles.AddRange(
                new IdentityUserRole<long> { UserId = deletingAdmin.Id, RoleId = administratorRoleId },
                new IdentityUserRole<long> { UserId = competingAdmin.Id, RoleId = administratorRoleId });
            await db.SaveChangesAsync(cancellationToken);
            return new Seed(club.ClubId, deletingAdmin.Id, competingAdmin.Id);
        }
    }

    /// <summary>Creates the Identity manager retained by the account deletion service.</summary>
    /// <returns>A substituted user manager.</returns>
    private static UserManager<NovaUserEntity> CreateUserManager()
        => Substitute.For<UserManager<NovaUserEntity>>(
            Substitute.For<IUserStore<NovaUserEntity>>(),
            Options.Create(new IdentityOptions()),
            new PasswordHasher<NovaUserEntity>(),
            Array.Empty<IUserValidator<NovaUserEntity>>(),
            Array.Empty<IPasswordValidator<NovaUserEntity>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<NovaUserEntity>>.Instance);

    /// <summary>Identifies one account-deletion race fixture.</summary>
    /// <param name="ClubId">The club identifier.</param>
    /// <param name="DeletingAdminUserId">The account attempting deletion.</param>
    /// <param name="CompetingAdminUserId">The administrator demoted by the competing transaction.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Seed(long ClubId, long DeletingAdminUserId, long CompetingAdminUserId);
}
