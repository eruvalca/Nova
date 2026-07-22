using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;
using Nova.Shared.Enums;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Campaign Lifecycle Event Entity Configuration.
/// </summary>
public class CampaignLifecycleEventEntityConfiguration : IEntityTypeConfiguration<CampaignLifecycleEventEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<CampaignLifecycleEventEntity> builder)
    {
        builder.HasKey(e => e.CampaignLifecycleEventId);
        builder.Property(e => e.CampaignLifecycleEventId)
            .ValueGeneratedOnAdd();

        var eventTypeColumn = $"\"{nameof(CampaignLifecycleEventEntity.EventType)}\"";

        builder.ToTable(tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "CK_CampaignLifecycleEvents_EventType",
                $"{eventTypeColumn} IN ({(int)CampaignLifecycleEventType.Closed}, {(int)CampaignLifecycleEventType.Reopened})"));

        builder
            .HasOne(e => e.Campaign)
            .WithMany(campaign => campaign.LifecycleEvents)
            .HasForeignKey(e => e.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.CampaignId);
    }
}
