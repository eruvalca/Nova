using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Features.Clubs;
using NSubstitute;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies PostgreSQL treats <c>%</c>, <c>_</c>, and <c>\</c> in club searches as literals rather
/// than <c>ILIKE</c> wildcards, and that plain substring and case-insensitive matching still work.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class ClubSearchEscapingPostgresTests(NovaAppHostFixture fixture)
{
    [Fact]
    public async Task SearchClubs_TreatsLikeMetacharactersAsLiterals()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;

        await using (var db = fixture.CreateAdminContext())
        {
            db.Clubs.AddRange(
                NewClub("50% Wins", actorUserId),
                NewClub("50 Losses", actorUserId),
                NewClub("a_b Squad", actorUserId),
                NewClub("axb Squad", actorUserId),
                NewClub(@"Path\Team", actorUserId),
                NewClub("PathTeam", actorUserId));
            await db.SaveChangesAsync(cancellationToken);
        }

        var service = new ClubService(
            new PostgresAdminContextFactory(fixture),
            new PostgresReadContextFactory(fixture),
            CreateUserManager(),
            fixture.CurrentUser,
            NullLogger<ClubService>.Instance);

        var percent = await service.SearchClubsAsync("50%", cancellationToken);
        percent.Value.Select(club => club.Name).ShouldBe(["50% Wins"]);

        var underscore = await service.SearchClubsAsync("a_b", cancellationToken);
        underscore.Value.Select(club => club.Name).ShouldBe(["a_b Squad"]);

        var backslash = await service.SearchClubsAsync(@"Path\T", cancellationToken);
        backslash.Value.Select(club => club.Name).ShouldBe([@"Path\Team"]);

        var caseInsensitive = await service.SearchClubsAsync("squad", cancellationToken);
        caseInsensitive.Value.Select(club => club.Name).ShouldBe(["a_b Squad", "axb Squad"], ignoreOrder: true);
    }

    private static ClubEntity NewClub(string name, long actorUserId) => new()
    {
        Name = name,
        City = "Austin",
        State = "TX",
        CreatedById = actorUserId
    };

    private static UserManager<NovaUserEntity> CreateUserManager()
        => Substitute.For<UserManager<NovaUserEntity>>(
            Substitute.For<IUserStore<NovaUserEntity>>(),
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<NovaUserEntity>>(),
            new List<IUserValidator<NovaUserEntity>>(),
            new List<IPasswordValidator<NovaUserEntity>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<NovaUserEntity>>>());
}
