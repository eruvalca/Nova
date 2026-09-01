using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for the Activity Event Entity.
/// </summary>
public class ActivityEventEntityConfiguration : IEntityTypeConfiguration<ActivityEventEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<ActivityEventEntity> builder)
    {
        builder.HasKey(e => e.ActivityEventId);
        builder.Property(e => e.ActivityEventId)
            .ValueGeneratedOnAdd();

        builder
            .HasOne(e => e.Club)
            .WithMany(c => c.ActivityEvents)
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.ActorDisplayName)
            .IsRequired()
            .HasMaxLength(201);

        builder.Property(e => e.PayloadJson)
            .IsRequired();

        builder.Property(e => e.EventKind)
            .HasConversion<int>();

        builder.HasIndex(e => new { e.ClubId, e.CreatedAt, e.ActivityEventId });
        builder.HasIndex(e => new { e.ClubId, e.CampaignId });
    }
}
