using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for <see cref="NovaUserEntity"/>: primary key, auto-generated <see cref="NovaUserEntity.Id"/>,
/// and the optional <see cref="NovaUserEntity.Club"/> foreign-key relationship (<see cref="DeleteBehavior.SetNull"/>).
/// </summary>
public class NovaUserEntityConfiguration : IEntityTypeConfiguration<NovaUserEntity>
{
    /// <summary>
    /// Configures the <see cref="NovaUserEntity"/> entity type: sets <see cref="NovaUserEntity.Id"/> as the
    /// primary key (value-generated on add) and declares the optional many-to-one relationship to
    /// <see cref="ClubEntity"/> with <see cref="DeleteBehavior.SetNull"/>.
    /// </summary>
    /// <param name="builder">The entity type builder.</param>
    public void Configure(EntityTypeBuilder<NovaUserEntity> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        builder
            .HasOne(u => u.Club)
            .WithMany(c => c.NovaUsers)
            .HasForeignKey(u => u.ClubId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
