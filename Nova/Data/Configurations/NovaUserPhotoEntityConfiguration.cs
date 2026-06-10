using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Nova User Photo Entity Configuration.
/// </summary>
public sealed class NovaUserPhotoEntityConfiguration : IEntityTypeConfiguration<NovaUserPhotoEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<NovaUserPhotoEntity> builder)
    {
        builder.HasKey(e => e.NovaUserPhotoId);
        builder.Property(e => e.NovaUserPhotoId)
            .ValueGeneratedOnAdd();

        builder
            .HasOne(e => e.NovaUser)
            .WithMany(u => u.Photos)
            .HasForeignKey(e => e.NovaUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
