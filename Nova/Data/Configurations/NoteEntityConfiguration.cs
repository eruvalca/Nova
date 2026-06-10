using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Note Entity Configuration.
/// </summary>
public class NoteEntityConfiguration : IEntityTypeConfiguration<NoteEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<NoteEntity> builder)
    {
        builder.HasKey(e => e.NoteId);
        builder.Property(e => e.NoteId)
            .ValueGeneratedOnAdd();

        builder
            .HasOne(e => e.Player)
            .WithMany(p => p.Notes)
            .HasForeignKey(e => e.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.Club)
            .WithMany()
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
