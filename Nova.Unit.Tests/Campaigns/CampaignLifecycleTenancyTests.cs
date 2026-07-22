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
    /// Verifies lifecycle events are visible only to the current club.
    /// </summary>
    [Fact]
    public void TenantContext_FiltersCampaignLifecycleEventsToCurrentClub()
    {
        ActAs(ClubAUserId, ClubAId);
        using var db = _harness.CreateTenantContext();

        var events = db.CampaignLifecycleEvents.ToList();

        events.Count.ShouldBe(1);
        events.ShouldAllBe(candidate => candidate.ClubId == ClubAId);
    }

    /// <summary>
    /// Verifies the save interceptor rejects lifecycle events explicitly assigned to another tenant.
    /// </summary>
    [Fact]
    public void TenantContext_RejectsCrossTenantCampaignLifecycleEventWrite()
    {
        ActAs(ClubAUserId, ClubAId);
        using var db = _harness.CreateTenantContext();
        db.CampaignLifecycleEvents.Add(new CampaignLifecycleEventEntity
        {
            CampaignId = 1200,
            EventType = CampaignLifecycleEventType.Closed,
            ClubId = ClubBId,
            CreatedById = ClubAUserId
        });

        var exception = Should.Throw<InvalidOperationException>(() => db.SaveChanges());

        exception.Message.ShouldContain("Cross-tenant");
    }

    /// <summary>
    /// Verifies the model carries campaign lifecycle status and event-type check constraints.
    /// </summary>
    [Fact]
    public void Model_ConfiguresCampaignLifecycleIntegrityMetadata()
    {
        using var db = _harness.CreateAdminContext();
        var model = db.GetService<IDesignTimeModel>().Model;

        var campaignEntityType = model.FindEntityType(typeof(CampaignEntity));
        campaignEntityType.ShouldNotBeNull();
        campaignEntityType.FindProperty(nameof(CampaignEntity.Status))!
            .IsConcurrencyToken.ShouldBeTrue();
        campaignEntityType.GetCheckConstraints()
            .ShouldContain(constraint => constraint.Name == "CK_Campaigns_StatusClosureMetadata");

        var eventEntityType = model.FindEntityType(typeof(CampaignLifecycleEventEntity));
        eventEntityType.ShouldNotBeNull();
        eventEntityType.GetCheckConstraints()
            .ShouldContain(constraint => constraint.Name == "CK_CampaignLifecycleEvents_EventType");
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
                ClubId = ClubAId,
                Name = "Campaign Lifecycle Club A",
                City = "Austin",
                State = "TX",
                CreatedById = ClubAUserId
            },
            new ClubEntity
            {
                ClubId = ClubBId,
                Name = "Campaign Lifecycle Club B",
                City = "Boston",
                State = "MA",
                CreatedById = ClubBUserId
            });

        db.Seasons.AddRange(
            new SeasonEntity
            {
                SeasonId = 1000,
                Name = "Season A",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAUserId
            },
            new SeasonEntity
            {
                SeasonId = 1001,
                Name = "Season B",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubBId,
                CreatedById = ClubBUserId
            });

        db.Campaigns.AddRange(
            new CampaignEntity
            {
                CampaignId = 1200,
                Name = "Campaign A",
                SeasonId = 1000,
                ClubId = ClubAId,
                CreatedById = ClubAUserId
            },
            new CampaignEntity
            {
                CampaignId = 1201,
                Name = "Campaign B",
                SeasonId = 1001,
                ClubId = ClubBId,
                CreatedById = ClubBUserId
            });

        db.CampaignLifecycleEvents.AddRange(
            new CampaignLifecycleEventEntity
            {
                CampaignLifecycleEventId = 1300,
                CampaignId = 1200,
                EventType = CampaignLifecycleEventType.Closed,
                ClubId = ClubAId,
                CreatedById = ClubAUserId
            },
            new CampaignLifecycleEventEntity
            {
                CampaignLifecycleEventId = 1301,
                CampaignId = 1201,
                EventType = CampaignLifecycleEventType.Reopened,
                ClubId = ClubBId,
                CreatedById = ClubBUserId
            });

        db.SaveChanges();
    }
}
