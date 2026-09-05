using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>Maps tenant-owned, uniquely identified placement commit evidence.</summary>
public class PlacementMutationReceiptEntityConfiguration : IEntityTypeConfiguration<PlacementMutationReceiptEntity>
{
    /// <summary>Configures receipt identity, retention lookup, and tenant ownership.</summary>
    /// <param name="builder">The receipt entity builder.</param>
    public void Configure(EntityTypeBuilder<PlacementMutationReceiptEntity> builder)
    {
        builder.HasKey(receipt => receipt.PlacementMutationReceiptId);
        builder.HasIndex(receipt => new { receipt.ClubId, receipt.OperationId }).IsUnique();
        builder.HasIndex(receipt => new { receipt.ClubId, receipt.CreatedAt });
        builder.HasIndex(receipt => receipt.CreatedAt);
    }
}
