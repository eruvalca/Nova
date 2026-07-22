using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;
using Nova.Shared.Enums;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Campaign Entity Configuration.
/// </summary>
public class CampaignEntityConfiguration : IEntityTypeConfiguration<CampaignEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<CampaignEntity> builder)
    {
        builder.HasKey(e => e.CampaignId);
        builder.Property(e => e.CampaignId)
            .ValueGeneratedOnAdd();
        builder.Property(e => e.Status)
            .IsConcurrencyToken();
        builder.HasAlternateKey(e => new { e.CampaignId, e.ClubId });

        var statusColumn = $"\"{nameof(CampaignEntity.Status)}\"";
        var closedAtColumn = $"\"{nameof(CampaignEntity.ClosedAt)}\"";
        var closedByIdColumn = $"\"{nameof(CampaignEntity.ClosedById)}\"";

        builder.ToTable(tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "CK_Campaigns_StatusClosureMetadata",
                $"({statusColumn} = {(int)CampaignStatus.Active} AND {closedAtColumn} IS NULL AND {closedByIdColumn} IS NULL) OR "
                + $"({statusColumn} = {(int)CampaignStatus.Closed} AND {closedAtColumn} IS NOT NULL AND {closedByIdColumn} IS NOT NULL)"));

        builder
            .HasOne(e => e.Club)
            .WithMany(c => c.Campaigns)
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.Season)
            .WithMany(s => s.Campaigns)
            .HasForeignKey(e => e.SeasonId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.SeasonId);
    }
}
