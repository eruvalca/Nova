using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Nova.Entities;
using Nova.Shared.Enums;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests campaign participation EF metadata and tenant isolation with the SQLite harness.
/// </summary>
public sealed class CampaignParticipationTenancyTests : IDisposable
{
    private const long ClubAId = 800;
    private const long ClubBId = 801;
    private const long ClubAUserId = 900;
    private const long ClubBUserId = 901;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes participation data for two tenants.
    /// </summary>
    public CampaignParticipationTenancyTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies participation rows are visible only to their owning club.
    /// </summary>
    [Fact]
    public void TenantContext_FiltersCampaignParticipationToCurrentClub()
    {
        ActAs(ClubAUserId, ClubAId);
        using var db = _harness.CreateTenantContext();

        var assignments = db.PlayerCampaignAssignments.ToList();

        assignments.Count.ShouldBe(1);
        assignments.ShouldAllBe(assignment => assignment.ClubId == ClubAId);
    }

    /// <summary>
    /// Verifies the save interceptor rejects participation explicitly assigned to another tenant.
    /// </summary>
    [Fact]
    public void TenantContext_RejectsCrossTenantCampaignParticipationWrite()
    {
        ActAs(ClubAUserId, ClubAId);
        using var db = _harness.CreateTenantContext();
        db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
        {
            PlayerId = 1101,
            CampaignId = 1201,
            ClubId = ClubBId,
            CreatedById = ClubAUserId
        });

        var exception = Should.Throw<InvalidOperationException>(() => db.SaveChanges());

        exception.Message.ShouldContain("Cross-tenant");
    }

    /// <summary>
    /// Verifies the model carries the required concurrency, uniqueness, filter, and check metadata.
    /// </summary>
    [Fact]
    public void Model_ConfiguresCampaignParticipationIntegrityMetadata()
    {
        using var db = _harness.CreateAdminContext();
        var model = db.GetService<IDesignTimeModel>().Model;
        var entityType = model.FindEntityType(typeof(PlayerCampaignAssignmentEntity));
        entityType.ShouldNotBeNull();

        entityType.FindProperty(nameof(PlayerCampaignAssignmentEntity.ConcurrencyToken))!
            .IsConcurrencyToken.ShouldBeTrue();

        var indexes = entityType.GetIndexes().ToList();
        indexes.ShouldContain(index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual(new[]
                {
                    nameof(PlayerCampaignAssignmentEntity.CampaignId),
                    nameof(PlayerCampaignAssignmentEntity.PlayerId)
                }));

        var tryoutIndex = indexes.Single(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[]
                {
                    nameof(PlayerCampaignAssignmentEntity.CampaignId),
                    nameof(PlayerCampaignAssignmentEntity.TryoutNumber)
                }));
        tryoutIndex.IsUnique.ShouldBeTrue();
        tryoutIndex.GetFilter().ShouldBe("\"TryoutNumber\" IS NOT NULL");

        entityType.GetCheckConstraints()
            .ShouldContain(constraint => constraint.Name == "CK_PlayerCampaignAssignments_PlacementOutcomeTeam");
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
    /// Seeds one participation for each of two clubs.
    /// </summary>
    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity
            {
                ClubId = ClubAId,
                Name = "Participation Club A",
                City = "Austin",
                State = "TX",
                CreatedById = ClubAUserId
            },
            new ClubEntity
            {
                ClubId = ClubBId,
                Name = "Participation Club B",
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
                PlayerCampaignAssignmentId = 1300,
                PlayerId = 1100,
                CampaignId = 1200,
                ClubId = ClubAId,
                PlacementOutcome = PlacementOutcome.Undecided,
                CreatedById = ClubAUserId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 1301,
                PlayerId = 1101,
                CampaignId = 1201,
                ClubId = ClubBId,
                PlacementOutcome = PlacementOutcome.Undecided,
                CreatedById = ClubBUserId
            });

        db.SaveChanges();
    }
}
