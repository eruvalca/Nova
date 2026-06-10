using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Components.Account;
using Nova.Data;
using Nova.Entities;
using Nova.Shared.Security;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Security;

/// <summary>
/// Tests for <see cref="NovaUserClaimsPrincipalFactory"/>: the ClubId claim is added only for
/// club members, and the HasProfilePhoto claim is added only when a photo row exists.
/// </summary>
public class NovaUserClaimsPrincipalFactoryTests : IDisposable
{
    private readonly TenancyTestHarness _harness = new();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GenerateClaims_AddsHasProfilePhotoClaim_WhenPhotoExists()
    {
        var user = SeedUser(id: 21, clubId: null);
        SeedPhoto(user.Id);
        var factory = CreateFactory();

        var principal = await factory.CreateAsync(user);

        principal.HasClaim(claim => claim.Type == NovaClaimTypes.HasProfilePhoto).ShouldBeTrue();
    }

    [Fact]
    public async Task GenerateClaims_OmitsHasProfilePhotoClaim_WhenNoPhotoExists()
    {
        var user = SeedUser(id: 22, clubId: null);
        var factory = CreateFactory();

        var principal = await factory.CreateAsync(user);

        principal.HasClaim(claim => claim.Type == NovaClaimTypes.HasProfilePhoto).ShouldBeFalse();
    }

    [Fact]
    public async Task GenerateClaims_AddsClubIdClaim_WhenUserHasClub()
    {
        SeedClub(id: 5);
        var user = SeedUser(id: 23, clubId: 5);
        var factory = CreateFactory();

        var principal = await factory.CreateAsync(user);

        principal.FindFirst(NovaClaimTypes.ClubId)?.Value.ShouldBe("5");
    }

    /// <summary>
    /// Builds the factory with a minimal real <see cref="UserManager{TUser}"/> over a substituted
    /// store and a context factory bound to the harness's shared SQLite database.
    /// </summary>
    /// <returns>The factory under test.</returns>
    private NovaUserClaimsPrincipalFactory CreateFactory()
    {
        var userStore = Substitute.For<IUserStore<NovaUserEntity>>();
        userStore.GetUserIdAsync(Arg.Any<NovaUserEntity>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.Arg<NovaUserEntity>().Id.ToString()));
        userStore.GetUserNameAsync(Arg.Any<NovaUserEntity>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<string?>($"user{call.Arg<NovaUserEntity>().Id}"));

        var userManager = new UserManager<NovaUserEntity>(
            userStore, Options.Create(new IdentityOptions()), null!, [], [], null!, null!, null!,
            NullLogger<UserManager<NovaUserEntity>>.Instance);

        var roleManager = new RoleManager<IdentityRole<long>>(
            Substitute.For<IRoleStore<IdentityRole<long>>>(), [], null!, null!,
            NullLogger<RoleManager<IdentityRole<long>>>.Instance);

        return new NovaUserClaimsPrincipalFactory(
            userManager,
            roleManager,
            Options.Create(new IdentityOptions()),
            new HarnessAdminContextFactory(_harness));
    }

    private NovaUserEntity SeedUser(long id, long? clubId)
    {
        using var context = _harness.CreateAdminContext();
        var user = new NovaUserEntity { Id = id, FirstName = "Test", LastName = "User", ClubId = clubId };
        context.Users.Add(user);
        context.SaveChanges();
        return user;
    }

    private void SeedClub(long id)
    {
        using var context = _harness.CreateAdminContext();
        context.Clubs.Add(new ClubEntity { ClubId = id, Name = $"Club {id}", City = "Austin", State = "TX", CreatedById = 1 });
        context.SaveChanges();
    }

    private void SeedPhoto(long userId)
    {
        using var context = _harness.CreateAdminContext();
        context.NovaUserPhotos.Add(new NovaUserPhotoEntity
        {
            OriginalBlobName = $"users/{userId}/test-original.jpg",
            NovaUserId = userId,
            CreatedById = userId
        });
        context.SaveChanges();
    }

    /// <summary>
    /// An <see cref="IDbContextFactory{TContext}"/> that hands out admin contexts from the harness.
    /// </summary>
    /// <param name="harness">The tenancy harness owning the shared SQLite connection.</param>
    private sealed class HarnessAdminContextFactory(TenancyTestHarness harness) : IDbContextFactory<NovaAdminDbContext>
    {
        /// <inheritdoc />
        public NovaAdminDbContext CreateDbContext() => harness.CreateAdminContext();
    }
}
