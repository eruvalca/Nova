using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Player Campaign Assignment Entity Configuration.
/// </summary>
public class PlayerCampaignAssignmentEntityConfiguration : IEntityTypeConfiguration<PlayerCampaignAssignmentEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<PlayerCampaignAssignmentEntity> builder)
    {
        builder.HasKey(e => e.PlayerCampaignAssignmentId);
        builder.Property(e => e.PlayerCampaignAssignmentId)
            .ValueGeneratedOnAdd();

        builder.Property(e => e.ConcurrencyToken)
            .IsConcurrencyToken();

        builder.HasIndex(e => new { e.CampaignId, e.PlayerId })
            .IsUnique();

        builder.HasIndex(e => new { e.CampaignId, e.TryoutNumber })
            .IsUnique()
            .HasFilter("\"TryoutNumber\" IS NOT NULL");

        builder.ToTable(tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "CK_PlayerCampaignAssignments_PlacementOutcomeTeam",
                "(\"PlacementOutcome\" = 1 AND \"TeamId\" IS NOT NULL) OR (\"PlacementOutcome\" IN (0, 2, 3) AND \"TeamId\" IS NULL)"));

        builder
            .HasOne(e => e.Player)
            .WithMany(p => p.CampaignAssignments)
            .HasForeignKey(e => e.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.Campaign)
            .WithMany(c => c.PlayerAssignments)
            .HasForeignKey(e => e.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.Team)
            .WithMany(t => t.PlayerAssignments)
            .HasForeignKey(e => e.TeamId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
