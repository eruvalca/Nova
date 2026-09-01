using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Tags;
using Nova.Shared.Features.Tags;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies PostgreSQL treats <c>%</c>, <c>_</c>, and <c>\</c> in tag-definition searches as literals
/// rather than <c>ILIKE</c> wildcards, and that plain substring matching still works.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TagDefinitionSearchEscapingPostgresTests(NovaAppHostFixture fixture)
{
    [Fact]
    public async Task GetManagementList_Search_TreatsLikeMetacharactersAsLiterals()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(cancellationToken);
        ActAs(seed.AdminUserId, seed.ClubId, isClubAdmin: true);

        var service = new TagDefinitionQueryService(
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<TagDefinitionQueryService>.Instance);

        var percent = await service.GetManagementListAsync(new GetTagDefinitionsInput { Search = "50%" }, cancellationToken);
        percent.Value.Items.Select(tag => tag.Name).ShouldBe(["50% Wins"]);

        var underscore = await service.GetManagementListAsync(new GetTagDefinitionsInput { Search = "a_b" }, cancellationToken);
        underscore.Value.Items.Select(tag => tag.Name).ShouldBe(["a_b Squad"]);

        var backslash = await service.GetManagementListAsync(new GetTagDefinitionsInput { Search = @"Path\T" }, cancellationToken);
        backslash.Value.Items.Select(tag => tag.Name).ShouldBe([@"Path\Team"]);

        var plain = await service.GetManagementListAsync(new GetTagDefinitionsInput { Search = "Squad" }, cancellationToken);
        plain.Value.Items.Select(tag => tag.Name).ShouldBe(["a_b Squad", "axb Squad"], ignoreOrder: true);
    }

    private async Task<Seed> SeedAsync(CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        var club = new ClubEntity { CreationOperationId = Guid.NewGuid(), Name = $"Tag Escaping Club {suffix}", City = "Austin", State = "TX", CreatedById = actorUserId };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        var admin = new NovaUserEntity { FirstName = "A", LastName = "Admin", ClubId = club.ClubId };
        db.Users.Add(admin);
        db.PlayerTags.AddRange(
            NewTag("50% Wins", club.ClubId, actorUserId),
            NewTag("50 Losses", club.ClubId, actorUserId),
            NewTag("a_b Squad", club.ClubId, actorUserId),
            NewTag("axb Squad", club.ClubId, actorUserId),
            NewTag(@"Path\Team", club.ClubId, actorUserId),
            NewTag("PathTeam", club.ClubId, actorUserId));
        await db.SaveChangesAsync(cancellationToken);

        return new Seed(club.ClubId, admin.Id);
    }

    private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    private static PlayerTagEntity NewTag(string name, long clubId, long createdById) => new()
    {
        CreationOperationId = Guid.NewGuid(),
        Name = name,
        NormalizedName = name.Trim().ToUpperInvariant(),
        Color = "#AABBCC",
        ClubId = clubId,
        CreatedById = createdById
    };

    private sealed record Seed(long ClubId, long AdminUserId);
}
