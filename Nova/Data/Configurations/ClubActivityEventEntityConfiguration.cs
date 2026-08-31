using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;
using Nova.Shared.Enums;

namespace Nova.Data.Configurations;

/// <summary>Configures the immutable tenant-scoped activity event table.</summary>
public sealed class ClubActivityEventEntityConfiguration : IEntityTypeConfiguration<ClubActivityEventEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClubActivityEventEntity> builder)
    {
        builder.HasKey(e => e.ClubActivityEventId);
        builder.Property(e => e.ClubActivityEventId).ValueGeneratedOnAdd();
        builder.Property(e => e.ActorDisplayName).HasMaxLength(201).IsRequired();
        builder.Property(e => e.SubjectDisplayName).HasMaxLength(201);
        builder.Property(e => e.CampaignName).HasMaxLength(100);
        builder.Property(e => e.SeasonName).HasMaxLength(100);
        builder.Property(e => e.PlayerDisplayName).HasMaxLength(201);
        builder.Property(e => e.PreviousTeamName).HasMaxLength(100);
        builder.Property(e => e.CurrentTeamName).HasMaxLength(100);
        builder.Property(e => e.PreviousSourceCampaignName).HasMaxLength(100);
        builder.Property(e => e.CurrentSourceCampaignName).HasMaxLength(100);

        builder.HasIndex(e => new { e.ClubId, e.Audience, e.CreatedAt, e.ClubActivityEventId });

        var kind = $"\"{nameof(ClubActivityEventEntity.EventKind)}\"";
        var audience = $"\"{nameof(ClubActivityEventEntity.Audience)}\"";
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "CK_ClubActivityEvents_EventKind",
                $"{kind} BETWEEN {(int)ClubActivityEventKind.CampaignDraftCreated} AND {(int)ClubActivityEventKind.MemberLeft}");
            table.HasCheckConstraint(
                "CK_ClubActivityEvents_Audience",
                $"{audience} IN ({(int)ClubActivityAudience.AllMembers}, {(int)ClubActivityAudience.Administrators})");
        });

        builder.HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
