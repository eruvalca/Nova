using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>Maps immutable proof and its global expiration index without aggregate foreign keys.</summary>
internal sealed class PlayerCreationReceiptEntityConfiguration : IEntityTypeConfiguration<PlayerCreationReceiptEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerCreationReceiptEntity> builder)
    {
        builder.HasKey(receipt => receipt.PlayerCreationReceiptId);
        builder.HasIndex(receipt => new { receipt.ClubId, receipt.OperationId }).IsUnique();
        builder.HasIndex(receipt => new { receipt.RecoveryExpiresAt, receipt.PlayerCreationReceiptId });
        builder.Property(receipt => receipt.RequestSha256).HasMaxLength(64);
    }
}
