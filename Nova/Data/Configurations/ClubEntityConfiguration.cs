using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Club Entity Configuration.
/// </summary>
public class ClubEntityConfiguration : IEntityTypeConfiguration<ClubEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<ClubEntity> builder)
    {
        builder.HasKey(e => e.ClubId);
        builder.Property(e => e.ClubId)
            .ValueGeneratedOnAdd();

        builder
            .HasMany(c => c.NovaUsers)
            .WithOne(u => u.Club)
            .HasForeignKey(u => u.ClubId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
