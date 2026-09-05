using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>Configures tenant-isolated immutable import proof and global expiration lookup.</summary>
public sealed class PlayerImportReceiptEntityConfiguration : IEntityTypeConfiguration<PlayerImportReceiptEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PlayerImportReceiptEntity> builder)
    {
        builder.HasKey(receipt => receipt.PlayerImportReceiptId);
        builder.HasIndex(receipt => new { receipt.ClubId, receipt.OperationId }).IsUnique();
        builder.HasIndex(receipt => receipt.CreatedAt);
        builder.HasIndex(receipt => receipt.RecoveryExpiresAt);
        builder.Property(receipt => receipt.FileSha256).HasMaxLength(64);
        builder.Property(receipt => receipt.ConfirmationTokenSha256).HasMaxLength(64);
    }
}
