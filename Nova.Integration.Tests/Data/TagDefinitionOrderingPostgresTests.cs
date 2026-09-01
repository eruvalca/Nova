using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Tags;
using Nova.Shared.Features.Tags;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies PostgreSQL enforces the tag-definition management-list cap and overflow probe:
/// <c>Items</c> is bounded to <see cref="TagDefinitionLimits.MaxTagDefinitions"/> and <c>HasMore</c> reports truncation.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TagDefinitionOrderingPostgresTests(NovaAppHostFixture fixture)
{
    [Fact]
    public async Task GetManagementList_BoundsItems_AndReportsOverflow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(TagDefinitionLimits.MaxTagDefinitions + 1, cancellationToken);
        ActAs(seed.AdminUserId, seed.ClubId, isClubAdmin: true);

        var service = new TagDefinitionQueryService(
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<TagDefinitionQueryService>.Instance);

        var result = await service.GetManagementListAsync(new GetTagDefinitionsInput(), cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(TagDefinitionLimits.MaxTagDefinitions);
        result.Value.HasMore.ShouldBeTrue();
        result.Value.Items.Select(tag => tag.Name).ShouldBeInOrder();
    }

    [Fact]
    public async Task GetManagementList_ReportsNoOverflow_WhenExactlyAtCap()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(TagDefinitionLimits.MaxTagDefinitions, cancellationToken);
        ActAs(seed.AdminUserId, seed.ClubId, isClubAdmin: true);

        var service = new TagDefinitionQueryService(
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<TagDefinitionQueryService>.Instance);

        var result = await service.GetManagementListAsync(new GetTagDefinitionsInput(), cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(TagDefinitionLimits.MaxTagDefinitions);
        result.Value.HasMore.ShouldBeFalse();
    }

    private async Task<Seed> SeedAsync(int tagCount, CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        var club = new ClubEntity { CreationOperationId = Guid.NewGuid(), Name = $"Tag Bound Club {suffix}", City = "Austin", State = "TX", CreatedById = actorUserId };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        var admin = new NovaUserEntity { FirstName = "A", LastName = "Admin", ClubId = club.ClubId };
        db.Users.Add(admin);
        for (var i = 0; i < tagCount; i++)
        {
            db.PlayerTags.Add(new PlayerTagEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Tag {i:000}",
                NormalizedName = $"Tag {i:000}".Trim().ToUpperInvariant(),
                Color = "#AABBCC",
                ClubId = club.ClubId,
                CreatedById = actorUserId
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return new Seed(club.ClubId, admin.Id);
    }

    private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    private sealed record Seed(long ClubId, long AdminUserId);
}
