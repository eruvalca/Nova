using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for campaign tag application removal receipts.
/// </summary>
public class CampaignTagApplicationRemovalReceiptEntityConfiguration : IEntityTypeConfiguration<CampaignTagApplicationRemovalReceiptEntity>
{
    /// <summary>
    /// Executes the configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<CampaignTagApplicationRemovalReceiptEntity> builder)
    {
        builder.HasKey(e => e.CampaignTagApplicationRemovalReceiptId);
        builder.Property(e => e.CampaignTagApplicationRemovalReceiptId)
            .ValueGeneratedOnAdd();

        builder.HasIndex(e => e.ClubId);
        builder.HasIndex(e => e.RemovalOperationId)
            .IsUnique();

        builder
            .HasOne(e => e.Club)
            .WithMany(club => club.CampaignTagApplicationRemovalReceipts)
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
