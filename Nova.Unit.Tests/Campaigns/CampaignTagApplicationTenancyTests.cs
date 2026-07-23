using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nova.Entities;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests campaign tag application tenant filtering, tenant-write guards, and EF relationship metadata.
/// </summary>
public sealed class CampaignTagApplicationTenancyTests : IDisposable
{
    private const long ClubAId = 800;
    private const long ClubBId = 801;
    private const long ClubAUserId = 900;
    private const long ClubBUserId = 901;
    private const long ClubAAssignmentId = 1300;
    private const long ClubBAssignmentId = 1301;
    private const long ClubATagId = 1400;
    private const long ClubBTagId = 1401;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes campaign tag application data for two tenants.
    /// </summary>
    public CampaignTagApplicationTenancyTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies campaign tag applications are visible only to their owning club.
    /// </summary>
    [Fact]
    public void TenantContext_FiltersCampaignTagApplicationsToCurrentClub()
    {
        ActAs(ClubAUserId, ClubAId);
        using var db = _harness.CreateTenantContext();

        var applications = db.CampaignTagApplications.ToList();

        applications.Count.ShouldBe(1);
        applications.ShouldAllBe(candidate => candidate.ClubId == ClubAId);
    }

    /// <summary>
    /// Verifies the save interceptor rejects campaign tag applications explicitly assigned to another tenant.
    /// </summary>
    [Fact]
    public void TenantContext_RejectsCrossTenantCampaignTagApplicationWrite()
    {
        ActAs(ClubAUserId, ClubAId);
        using var db = _harness.CreateTenantContext();
        db.CampaignTagApplications.Add(new CampaignTagApplicationEntity
        {
            PlayerCampaignAssignmentId = ClubBAssignmentId,
            PlayerTagId = ClubBTagId,
            ClubId = ClubBId,
            CreatedById = ClubAUserId
        });

        var exception = Should.Throw<InvalidOperationException>(() => db.SaveChanges());

        exception.Message.ShouldContain("Cross-tenant");
    }

    /// <summary>
    /// Verifies campaign tag applications enforce unique participation/tag pairs and same-club composite relationships.
    /// </summary>
    [Fact]
    public void Model_ConfiguresCampaignTagApplicationIntegrityMetadata()
    {
        using var db = _harness.CreateAdminContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var entityType = model.FindEntityType(typeof(CampaignTagApplicationEntity));
        entityType.ShouldNotBeNull();

        var indexes = entityType.GetIndexes().ToList();
        indexes.ShouldContain(index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[]
                {
                    nameof(CampaignTagApplicationEntity.PlayerCampaignAssignmentId),
                    nameof(CampaignTagApplicationEntity.PlayerTagId)
                }));

        var assignmentForeignKey = entityType.GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(PlayerCampaignAssignmentEntity));
        assignmentForeignKey.Properties.Select(property => property.Name)
            .ShouldBe(
            [
                nameof(CampaignTagApplicationEntity.PlayerCampaignAssignmentId),
                nameof(CampaignTagApplicationEntity.ClubId)
            ]);

        var tagForeignKey = entityType.GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(PlayerTagEntity));
        tagForeignKey.Properties.Select(property => property.Name)
            .ShouldBe(
            [
                nameof(CampaignTagApplicationEntity.PlayerTagId),
                nameof(CampaignTagApplicationEntity.ClubId)
            ]);
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
    /// Seeds one campaign tag application for each club.
    /// </summary>
    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity
            {
                ClubId = ClubAId,
                Name = "Tag App Club A",
                City = "Austin",
                State = "TX",
                CreatedById = ClubAUserId
            },
            new ClubEntity
            {
                ClubId = ClubBId,
                Name = "Tag App Club B",
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

        db.Players.AddRange(
            new PlayerEntity
            {
                PlayerId = 1100,
                FirstName = "Club",
                LastName = "A Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAUserId
            },
            new PlayerEntity
            {
                PlayerId = 1101,
                FirstName = "Club",
                LastName = "B Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
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

        db.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ClubAAssignmentId,
                PlayerId = 1100,
                CampaignId = 1200,
                ClubId = ClubAId,
                CreatedById = ClubAUserId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ClubBAssignmentId,
                PlayerId = 1101,
                CampaignId = 1201,
                ClubId = ClubBId,
                CreatedById = ClubBUserId
            });

        db.PlayerTags.AddRange(
            new PlayerTagEntity
            {
                PlayerTagId = ClubATagId,
                Name = "A Tag",
                Color = "#00AA00",
                ClubId = ClubAId,
                CreatedById = ClubAUserId
            },
            new PlayerTagEntity
            {
                PlayerTagId = ClubBTagId,
                Name = "B Tag",
                Color = "#0000AA",
                ClubId = ClubBId,
                CreatedById = ClubBUserId
            });

        db.CampaignTagApplications.AddRange(
            new CampaignTagApplicationEntity
            {
                CampaignTagApplicationId = 1500,
                PlayerCampaignAssignmentId = ClubAAssignmentId,
                PlayerTagId = ClubATagId,
                ClubId = ClubAId,
                CreatedById = ClubAUserId
            },
            new CampaignTagApplicationEntity
            {
                CampaignTagApplicationId = 1501,
                PlayerCampaignAssignmentId = ClubBAssignmentId,
                PlayerTagId = ClubBTagId,
                ClubId = ClubBId,
                CreatedById = ClubBUserId
            });

        db.SaveChanges();
    }
}
