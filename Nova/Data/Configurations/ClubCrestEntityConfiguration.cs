using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Club Crest Entity Configuration.
/// </summary>
public sealed class ClubCrestEntityConfiguration : IEntityTypeConfiguration<ClubCrestEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<ClubCrestEntity> builder)
    {
        builder.HasKey(e => e.ClubCrestId);
        builder.Property(e => e.ClubCrestId)
            .ValueGeneratedOnAdd();

        // Each club has at most one crest row; the unique index turns the service's
        // check-then-insert race into a DbUpdateException its catch block already handles.
        builder
            .HasIndex(e => e.ClubId)
            .IsUnique();

        builder
            .HasOne(e => e.Club)
            .WithOne(c => c.ClubCrest)
            .HasForeignKey<ClubCrestEntity>(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
