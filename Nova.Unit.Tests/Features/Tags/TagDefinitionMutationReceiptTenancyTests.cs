using Nova.Entities;
using Nova.Shared.Enums;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Features.Tags;

/// <summary>
/// Tests tag-definition mutation receipt tenant filtering and tenant-write guards.
/// </summary>
public sealed class TagDefinitionMutationReceiptTenancyTests : IDisposable
{
    private const long ClubAId = 800;
    private const long ClubBId = 801;
    private const long ClubAUserId = 900;
    private const long ClubBUserId = 901;
    private const long ClubATagId = 1400;
    private const long ClubBTagId = 1401;

    private readonly TenancyTestHarness _harness = new();

    // Assigned during Seed() so tests reference the same durable operation identifiers.
    private Guid _clubAOperationId;
    private Guid _clubBOperationId;

    /// <summary>
    /// Initializes tag-definition mutation receipt data for two tenants.
    /// </summary>
    public TagDefinitionMutationReceiptTenancyTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies mutation receipts are visible only to their owning club.
    /// </summary>
    [Fact]
    public void TenantContext_FiltersMutationReceiptsToCurrentClub()
    {
        ActAs(ClubAUserId, ClubAId);
        using var db = _harness.CreateTenantContext();

        var receipts = db.TagDefinitionMutationReceipts.ToList();

        receipts.Count.ShouldBe(1);
        receipts.ShouldAllBe(receipt => receipt.ClubId == ClubAId);
    }

    /// <summary>
    /// Verifies the save interceptor rejects mutation receipts explicitly assigned to another tenant.
    /// </summary>
    [Fact]
    public void TenantContext_RejectsCrossTenantMutationReceiptWrite()
    {
        ActAs(ClubAUserId, ClubAId);
        using var db = _harness.CreateTenantContext();
        db.TagDefinitionMutationReceipts.Add(new TagDefinitionMutationReceiptEntity
        {
            OperationId = Guid.CreateVersion7(),
            PlayerTagId = ClubBTagId,
            MutationType = TagDefinitionMutationType.Updated,
            ClubId = ClubBId,
            CreatedById = ClubAUserId
        });

        var exception = Should.Throw<InvalidOperationException>(() => db.SaveChanges());

        exception.Message.ShouldContain("Cross-tenant");
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
    /// Seeds one tag definition and one mutation receipt for each club.
    /// </summary>
    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                ClubId = ClubAId,
                Name = "Tag Receipt Club A",
                City = "Austin",
                State = "TX",
                CreatedById = ClubAUserId
            },
            new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                ClubId = ClubBId,
                Name = "Tag Receipt Club B",
                City = "Boston",
                State = "MA",
                CreatedById = ClubBUserId
            });

        db.PlayerTags.AddRange(
            new PlayerTagEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerTagId = ClubATagId,
                Name = "A Tag",
                NormalizedName = "A TAG",
                Color = "#00AA00",
                ClubId = ClubAId,
                CreatedById = ClubAUserId
            },
            new PlayerTagEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerTagId = ClubBTagId,
                Name = "B Tag",
                NormalizedName = "B TAG",
                Color = "#0000AA",
                ClubId = ClubBId,
                CreatedById = ClubBUserId
            });

        _clubAOperationId = Guid.CreateVersion7();
        _clubBOperationId = Guid.CreateVersion7();
        db.TagDefinitionMutationReceipts.AddRange(
            new TagDefinitionMutationReceiptEntity
            {
                TagDefinitionMutationReceiptId = 1600,
                OperationId = _clubAOperationId,
                PlayerTagId = ClubATagId,
                MutationType = TagDefinitionMutationType.Archived,
                ClubId = ClubAId,
                CreatedById = ClubAUserId
            },
            new TagDefinitionMutationReceiptEntity
            {
                TagDefinitionMutationReceiptId = 1601,
                OperationId = _clubBOperationId,
                PlayerTagId = ClubBTagId,
                MutationType = TagDefinitionMutationType.Restored,
                ClubId = ClubBId,
                CreatedById = ClubBUserId
            });

        db.SaveChanges();
    }
}
