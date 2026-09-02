using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Season Entity Configuration.
/// </summary>
public class SeasonEntityConfiguration : IEntityTypeConfiguration<SeasonEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<SeasonEntity> builder)
    {
        builder.HasKey(e => e.SeasonId);
        builder.Property(e => e.SeasonId)
            .ValueGeneratedOnAdd();
        builder.Property(e => e.ConcurrencyToken)
            .IsConcurrencyToken();
        builder.HasAlternateKey(e => new { e.SeasonId, e.ClubId });

        builder
            .HasOne(e => e.Club)
            .WithMany(c => c.Seasons)
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne<SeasonEntity>()
            .WithMany()
            .HasForeignKey(e => new { e.CreationPreviousSeasonId, e.ClubId })
            .HasPrincipalKey(e => new { e.SeasonId, e.ClubId })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(e => new { e.ClubId, e.Name }).IsUnique();
        builder.HasIndex(e => new { e.ClubId, e.CreationOperationId })
            .IsUnique();
    }
}
