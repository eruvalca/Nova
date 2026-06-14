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
    }
}
