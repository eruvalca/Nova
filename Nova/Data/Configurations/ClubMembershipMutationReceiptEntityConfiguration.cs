using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>Configures durable club-membership mutation receipts.</summary>
public class ClubMembershipMutationReceiptEntityConfiguration : IEntityTypeConfiguration<ClubMembershipMutationReceiptEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubMembershipMutationReceiptEntity> builder)
    {
        builder.HasKey(receipt => receipt.ClubMembershipMutationReceiptId);
        builder.Property(receipt => receipt.ClubMembershipMutationReceiptId).ValueGeneratedOnAdd();
        builder.Property(receipt => receipt.MutationKind).HasMaxLength(32);
        builder.HasIndex(receipt => receipt.CreatedAt);
        builder.HasIndex(receipt => receipt.OperationId).IsUnique();
    }
}
