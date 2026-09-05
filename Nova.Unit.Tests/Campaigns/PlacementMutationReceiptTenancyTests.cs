using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacementServiceTests
{
    /// <summary>Checks placement receipts are visible only within their club through write and read contexts.</summary>
    [Fact]
    public void PlacementMutationReceipts_FilterByOwningTenant()
    {
        using (var seed = _harness.CreateAdminContext())
        {
            seed.PlacementMutationReceipts.AddRange(
                new PlacementMutationReceiptEntity { OperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = ClubAAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubAId, CreatedById = ClubAAdminId },
                new PlacementMutationReceiptEntity { OperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = ClubBAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubBId, CreatedById = ClubBAdminId });
            seed.SaveChanges();
        }
        ActAs(ClubAMemberId, ClubAId);
        using var tenant = _harness.CreateTenantContext();
        tenant.PlacementMutationReceipts.Single().PlayerCampaignAssignmentId.ShouldBe(ClubAAssignmentId);
        using var read = _harness.CreateReadContext();
        read.PlacementMutationReceipts.Single().PlayerCampaignAssignmentId.ShouldBe(ClubAAssignmentId);
        ActAs(ClubBAdminId, ClubBId, isClubAdmin: true);
        using var other = _harness.CreateTenantContext();
        other.PlacementMutationReceipts.Single().PlayerCampaignAssignmentId.ShouldBe(ClubBAssignmentId);
    }

    /// <summary>Checks successful saves prune only expired receipts belonging to the acting club.</summary>
    [Fact]
    public async Task PlacementMutationReceipts_PruneExpiredReceipts_WithinCurrentTenantOnly()
    {
        var expiredOperation = Guid.NewGuid();
        var recentOperation = Guid.NewGuid();
        var otherOperation = Guid.NewGuid();
        using (var seed = _harness.CreateAdminContext())
        {
            var expired = new PlacementMutationReceiptEntity { OperationId = expiredOperation, PlayerCampaignAssignmentId = ClubAAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubAId, CreatedById = ClubAAdminId };
            var recent = new PlacementMutationReceiptEntity { OperationId = recentOperation, PlayerCampaignAssignmentId = ClubAAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubAId, CreatedById = ClubAAdminId };
            var other = new PlacementMutationReceiptEntity { OperationId = otherOperation, PlayerCampaignAssignmentId = ClubBAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubBId, CreatedById = ClubBAdminId };
            seed.PlacementMutationReceipts.AddRange(expired, recent, other);
            seed.SaveChanges();
            seed.PlacementMutationReceipts.Where(receipt => receipt.OperationId == expiredOperation || receipt.OperationId == otherOperation)
                .ExecuteUpdate(setters => setters.SetProperty(receipt => receipt.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2)));
            seed.PlacementMutationReceipts.Where(receipt => receipt.OperationId == recentOperation)
                .ExecuteUpdate(setters => setters.SetProperty(receipt => receipt.CreatedAt, DateTimeOffset.UtcNow.AddHours(-12)));
        }
        ActAs(ClubAMemberId, ClubAId);

        (await SaveAsync(Nova.Shared.Enums.PlacementOutcome.NotSelected, _clubAConcurrencyToken)).Value
            .ShouldBeOfType<Nova.Shared.Features.Campaigns.PlacementMutationSuccess>();

        using var verify = _harness.CreateAdminContext();
        var operations = verify.PlacementMutationReceipts.Select(receipt => receipt.OperationId).ToList();
        operations.ShouldNotContain(expiredOperation);
        operations.ShouldContain(recentOperation);
        operations.ShouldContain(otherOperation);
        operations.Count.ShouldBe(3);
    }
    /// <summary>Checks committed receipts cannot be rewritten to claim a different mutation result.</summary>
    [Fact]
    public async Task PlacementMutationReceipts_RejectChangesToCommittedReceipt()
    {
        ActAs(ClubAMemberId, ClubAId);
        var saved = (await SaveAsync(Nova.Shared.Enums.PlacementOutcome.NotSelected, _clubAConcurrencyToken)).Value
            .ShouldBeOfType<Nova.Shared.Features.Campaigns.PlacementMutationSuccess>();
        using var tenant = _harness.CreateTenantContext();
        var receipt = tenant.PlacementMutationReceipts.Single();
        var operationId = receipt.OperationId;
        receipt.ConcurrencyToken = Guid.NewGuid();

        Should.Throw<InvalidOperationException>(() => tenant.SaveChanges());

        using var verify = _harness.CreateAdminContext();
        var persisted = verify.PlacementMutationReceipts.Single();
        persisted.OperationId.ShouldBe(operationId);
        persisted.ConcurrencyToken.ShouldBe(saved.ConcurrencyToken);
    }
    /// <summary>Checks immutable receipt evidence survives deletion of its former owning club.</summary>
    [Fact]
    public void PlacementMutationReceipts_SurviveOwningClubDeletion()
    {
        var operationId = Guid.NewGuid();
        var token = Guid.NewGuid();
        using (var seed = _harness.CreateAdminContext())
        {
            seed.PlacementMutationReceipts.Add(new PlacementMutationReceiptEntity
            {
                OperationId = operationId,
                PlayerCampaignAssignmentId = ClubBAssignmentId,
                ConcurrencyToken = token,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });
            seed.SaveChanges();
        }
        using (var delete = _harness.CreateAdminContext())
        {
            delete.Clubs.Remove(delete.Clubs.Single(club => club.ClubId == ClubBId));
            delete.SaveChanges();
        }

        using var verify = _harness.CreateAdminContext();
        verify.Clubs.Any(club => club.ClubId == ClubBId).ShouldBeFalse();
        var receipt = verify.PlacementMutationReceipts.Single();
        receipt.ClubId.ShouldBe(ClubBId);
        receipt.OperationId.ShouldBe(operationId);
        receipt.ConcurrencyToken.ShouldBe(token);
        receipt.PlayerCampaignAssignmentId.ShouldBe(ClubBAssignmentId);
    }

    /// <summary>Checks global cleanup removes expired orphan receipts while retaining fresh evidence.</summary>
    [Fact]
    public async Task PlacementMutationReceipts_GlobalCleanupRemovesExpiredDeletedClubEvidence()
    {
        var expiredOperation = Guid.NewGuid();
        var freshOperation = Guid.NewGuid();
        using (var seed = _harness.CreateAdminContext())
        {
            seed.PlacementMutationReceipts.AddRange(
                new PlacementMutationReceiptEntity { OperationId = expiredOperation, PlayerCampaignAssignmentId = ClubBAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubBId, CreatedById = ClubBAdminId },
                new PlacementMutationReceiptEntity { OperationId = freshOperation, PlayerCampaignAssignmentId = ClubAAssignmentId, ConcurrencyToken = Guid.NewGuid(), ClubId = ClubAId, CreatedById = ClubAAdminId });
            seed.SaveChanges();
            seed.PlacementMutationReceipts.Where(receipt => receipt.OperationId == expiredOperation)
                .ExecuteUpdate(setters => setters.SetProperty(receipt => receipt.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2)));
        }
        using (var delete = _harness.CreateAdminContext())
        {
            delete.Clubs.Remove(delete.Clubs.Single(club => club.ClubId == ClubBId));
            delete.SaveChanges();
        }
        ActAs(ClubAMemberId, ClubAId);
        using (var cleanup = _harness.CreateAdminContext())
        {
            cleanup.PlacementMutationReceipts.Count().ShouldBe(2);
            await Nova.Features.Account.ClubMembershipMutationReceipts.PruneExpiredAsync(cleanup, TestContext.Current.CancellationToken);
            await cleanup.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var verify = _harness.CreateAdminContext();
        verify.PlacementMutationReceipts.Single().OperationId.ShouldBe(freshOperation);
        verify.Clubs.Any(club => club.ClubId == ClubBId).ShouldBeFalse();
    }
    /// <summary>Checks explicit receipt writes for another club are rejected before persistence.</summary>
    [Fact]
    public void PlacementMutationReceipts_RejectCrossTenantWrites()
    {
        ActAs(ClubAMemberId, ClubAId);
        using var tenant = _harness.CreateTenantContext();
        tenant.PlacementMutationReceipts.Add(new PlacementMutationReceiptEntity
        {
            OperationId = Guid.NewGuid(),
            PlayerCampaignAssignmentId = ClubBAssignmentId,
            ConcurrencyToken = Guid.NewGuid(),
            ClubId = ClubBId,
            CreatedById = ClubAMemberId
        });
        Should.Throw<InvalidOperationException>(() => tenant.SaveChanges()).Message.ShouldContain("Cross-tenant");
        using var verify = _harness.CreateAdminContext();
        verify.PlacementMutationReceipts.Count().ShouldBe(0);
    }
}
