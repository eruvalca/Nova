using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for <see cref="NoteEntity"/>, associating evaluation notes
/// with campaign participation as the dependent side of the relationship.
/// </summary>
public class NoteEntityConfiguration : IEntityTypeConfiguration<NoteEntity>
{
    /// <summary>
    /// Applies the entity configuration.
    /// </summary>
    /// <param name="builder">The entity type builder.</param>
    public void Configure(EntityTypeBuilder<NoteEntity> builder)
    {
        builder.HasKey(e => e.NoteId);
        builder.Property(e => e.NoteId)
            .ValueGeneratedOnAdd();

        builder
            .HasOne(e => e.PlayerCampaignAssignment)
            .WithMany(p => p.Notes)
            .HasForeignKey(e => e.PlayerCampaignAssignmentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
