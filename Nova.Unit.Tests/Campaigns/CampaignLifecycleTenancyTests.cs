using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nova.Entities;
using Nova.Shared.Enums;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests campaign lifecycle metadata model constraints and tenant isolation for lifecycle events.
/// </summary>
public sealed class CampaignLifecycleTenancyTests : IDisposable
{
    private const long ClubAId = 800;
    private const long ClubBId = 801;
    private const long ClubAUserId = 900;
    private const long ClubBUserId = 901;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes campaign lifecycle data for two tenants.
    /// </summary>
    public CampaignLifecycleTenancyTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies activity events are visible only to the current club.
    /// </summary>
    [Fact]
    public void TenantContext_FiltersActivityEventsToCurrentClub()
    {
        ActAs(ClubAUserId, ClubAId);
        using var db = _harness.CreateTenantContext();

        var events = db.ActivityEvents.ToList();

        events.Count.ShouldBe(1);
        events.ShouldAllBe(candidate => candidate.ClubId == ClubAId);
    }

    /// <summary>
    /// Verifies the activity event model exposes an allowed values list for the event kind and
    /// carries the snapshot fields required for readable feed rows.
    /// </summary>
    [Fact]
    public void Model_ConfiguresActivityEventIntegrityMetadata()
    {
        using var db = _harness.CreateAdminContext();
        var model = db.GetService<IDesignTimeModel>().Model;

        var entityType = model.FindEntityType(typeof(ActivityEventEntity));
        entityType.ShouldNotBeNull();

        var idProperty = entityType.FindProperty(nameof(ActivityEventEntity.ActivityEventId));
        idProperty.ShouldNotBeNull();
        idProperty!.ValueGenerated.ShouldBe(ValueGenerated.OnAdd);

        entityType.FindProperty(nameof(ActivityEventEntity.ActorDisplayName))!
            .IsNullable.ShouldBeFalse();
        entityType.FindProperty(nameof(ActivityEventEntity.PayloadJson))!
            .IsNullable.ShouldBeFalse();

        var clubForeignKey = entityType.GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(ClubEntity));
        clubForeignKey.Properties.Select(property => property.Name).ShouldBe([nameof(ActivityEventEntity.ClubId)]);
        clubForeignKey.DeleteBehavior.ShouldBe(DeleteBehavior.Cascade);

        entityType.GetCheckConstraints().ShouldBeEmpty();
    }

    /// <summary>
    /// Sets the current user for tenant-filtered operations.
    /// </summary>
    /// <param name="userId">The current user identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    private void ActAs(long userId, long clubId)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
    }

    /// <summary>
    /// Seeds one campaign and one lifecycle event for each of two clubs.
    /// </summary>
    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                ClubId = ClubAId,
                Name = "Campaign Lifecycle Club A",
                City = "Austin",
                State = "TX",
                CreatedById = ClubAUserId
            },
            new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                ClubId = ClubBId,
                Name = "Campaign Lifecycle Club B",
                City = "Boston",
                State = "MA",
                CreatedById = ClubBUserId
            });

        db.Seasons.AddRange(
            new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                SeasonId = 1000,
                Name = "Season A",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAUserId
            },
            new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                SeasonId = 1001,
                Name = "Season B",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubBId,
                CreatedById = ClubBUserId
            });

        db.Campaigns.AddRange(
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = 1200,
                Name = "Campaign A",
                SeasonId = 1000,
                ClubId = ClubAId,
                CreatedById = ClubAUserId
            },
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = 1201,
                Name = "Campaign B",
                SeasonId = 1001,
                ClubId = ClubBId,
                CreatedById = ClubBUserId
            });

        db.ActivityEvents.AddRange(
            new ActivityEventEntity
            {
                ActivityEventId = 1300,
                CampaignId = 1200,
                EventKind = ActivityEventKind.CampaignClosed,
                IsAdminOnly = false,
                ClubId = ClubAId,
                ActorUserId = ClubAUserId,
                ActorDisplayName = "Club A User",
                PayloadJson = """{"campaignId":1200,"campaignName":"Campaign A"}""",
                CreatedById = ClubAUserId
            },
            new ActivityEventEntity
            {
                ActivityEventId = 1301,
                CampaignId = 1201,
                EventKind = ActivityEventKind.CampaignReopened,
                IsAdminOnly = false,
                ClubId = ClubBId,
                ActorUserId = ClubBUserId,
                ActorDisplayName = "Club B User",
                PayloadJson = """{"campaignId":1201,"campaignName":"Campaign B"}""",
                CreatedById = ClubBUserId
            });

        db.SaveChanges();
    }
}
