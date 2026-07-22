using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for campaign tag applications.
/// </summary>
public class CampaignTagApplicationEntityConfiguration : IEntityTypeConfiguration<CampaignTagApplicationEntity>
{
    /// <summary>
    /// Executes the configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<CampaignTagApplicationEntity> builder)
    {
        builder.HasKey(e => e.CampaignTagApplicationId);
        builder.Property(e => e.CampaignTagApplicationId)
            .ValueGeneratedOnAdd();

        builder.HasIndex(e => new { e.PlayerCampaignAssignmentId, e.PlayerTagId })
            .IsUnique();

        builder
            .HasOne(e => e.PlayerCampaignAssignment)
            .WithMany(assignment => assignment.CampaignTagApplications)
            .HasForeignKey(e => new { e.PlayerCampaignAssignmentId, e.ClubId })
            .HasPrincipalKey(assignment => new { assignment.PlayerCampaignAssignmentId, assignment.ClubId })
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.PlayerTag)
            .WithMany(tag => tag.CampaignTagApplications)
            .HasForeignKey(e => new { e.PlayerTagId, e.ClubId })
            .HasPrincipalKey(tag => new { tag.PlayerTagId, tag.ClubId })
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.Club)
            .WithMany(club => club.CampaignTagApplications)
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
