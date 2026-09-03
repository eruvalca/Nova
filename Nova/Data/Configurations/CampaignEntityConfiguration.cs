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
    /// Gets the database constraint name that enforces one Active campaign per club.
    /// </summary>
    public const string OneActiveCampaignPerClubIndexName = "UX_Campaigns_ClubId_Active";

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
        builder.HasIndex(e => new { e.ClubId, e.CreationOperationId })
            .IsUnique();
        builder.HasIndex(e => new { e.ClubId, e.SeasonId, e.Name })
            .IsUnique();
        builder.HasIndex(e => e.ClubId)
            .HasDatabaseName(OneActiveCampaignPerClubIndexName)
            .HasFilter($"\"{nameof(CampaignEntity.Status)}\" = {(int)CampaignStatus.Active}")
            .IsUnique();

        var statusColumn = $"\"{nameof(CampaignEntity.Status)}\"";
        var closedAtColumn = $"\"{nameof(CampaignEntity.ClosedAt)}\"";
        var closedByIdColumn = $"\"{nameof(CampaignEntity.ClosedById)}\"";

        builder.ToTable(tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "CK_Campaigns_StatusClosureMetadata",
                $"({statusColumn} IN ({(int)CampaignStatus.Active}, {(int)CampaignStatus.Draft}) AND {closedAtColumn} IS NULL AND {closedByIdColumn} IS NULL) OR "
                + $"({statusColumn} = {(int)CampaignStatus.Closed} AND {closedAtColumn} IS NOT NULL AND {closedByIdColumn} IS NOT NULL)"));

        builder
            .HasOne(e => e.Club)
            .WithMany(c => c.Campaigns)
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.Season)
            .WithMany(s => s.Campaigns)
            .HasPrincipalKey(s => new { s.SeasonId, s.ClubId })
            .HasForeignKey(e => new { e.SeasonId, e.ClubId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.SeasonId);
    }
}
