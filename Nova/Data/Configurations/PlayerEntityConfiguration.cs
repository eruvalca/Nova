using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;
using Nova.Shared.Enums;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Player Entity Configuration.
/// </summary>
public class PlayerEntityConfiguration : IEntityTypeConfiguration<PlayerEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<PlayerEntity> builder)
    {
        builder.HasKey(e => e.PlayerId);
        builder.Property(e => e.PlayerId)
            .ValueGeneratedOnAdd();
        builder.Property(e => e.LifecycleStatus)
            .IsConcurrencyToken();

        var lifecycleStatusColumn = $"\"{nameof(PlayerEntity.LifecycleStatus)}\"";
        var archivedAtColumn = $"\"{nameof(PlayerEntity.ArchivedAt)}\"";
        var archivedByIdColumn = $"\"{nameof(PlayerEntity.ArchivedById)}\"";

        builder.ToTable(tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "CK_Players_LifecycleArchiveMetadata",
                $"({lifecycleStatusColumn} = {(int)LifecycleStatus.Active} AND {archivedAtColumn} IS NULL AND {archivedByIdColumn} IS NULL) OR "
                + $"({lifecycleStatusColumn} = {(int)LifecycleStatus.Archived} AND {archivedAtColumn} IS NOT NULL AND {archivedByIdColumn} IS NOT NULL)"));

        builder
            .HasOne(e => e.Club)
            .WithMany(c => c.Players)
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(p => p.Tags)
            .WithMany()
            .UsingEntity(
                right => right.HasOne(typeof(PlayerTagEntity)).WithMany().OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne(typeof(PlayerEntity)).WithMany().OnDelete(DeleteBehavior.Cascade)
            );
    }
}
