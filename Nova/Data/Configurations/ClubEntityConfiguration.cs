using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for <see cref="ClubEntity"/>: primary key and auto-generated <see cref="ClubEntity.ClubId"/>.
/// </summary>
public class ClubEntityConfiguration : IEntityTypeConfiguration<ClubEntity>
{
    /// <summary>
    /// Configures the <see cref="ClubEntity"/> entity type: sets <see cref="ClubEntity.ClubId"/> as the
    /// primary key and marks it as value-generated on add.
    /// </summary>
    /// <param name="builder">The entity type builder.</param>
    public void Configure(EntityTypeBuilder<ClubEntity> builder)
    {
        builder.HasKey(e => e.ClubId);
        builder.Property(e => e.ClubId)
            .ValueGeneratedOnAdd();

        // Unique per creator per logical operation so an ambiguous-commit retry can find (and
        // verify, not replay) the club created by the exact operation, even if a retry attempt
        // would otherwise insert a second club for the same user.
        builder.HasIndex(e => new { e.CreatedById, e.CreationOperationId })
            .IsUnique()
            .HasFilter("\"CreationOperationId\" IS NOT NULL");
    }
}
