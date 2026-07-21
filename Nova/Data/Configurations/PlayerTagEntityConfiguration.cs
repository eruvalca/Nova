using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;
using Nova.Shared.Enums;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Player Tag Entity Configuration.
/// </summary>
public class PlayerTagEntityConfiguration : IEntityTypeConfiguration<PlayerTagEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<PlayerTagEntity> builder)
    {
        builder.HasKey(e => e.PlayerTagId);
        builder.Property(e => e.PlayerTagId)
            .ValueGeneratedOnAdd();
        builder.Property(e => e.LifecycleStatus)
            .IsConcurrencyToken();

        var lifecycleStatusColumn = $"\"{nameof(PlayerTagEntity.LifecycleStatus)}\"";
        var archivedAtColumn = $"\"{nameof(PlayerTagEntity.ArchivedAt)}\"";
        var archivedByIdColumn = $"\"{nameof(PlayerTagEntity.ArchivedById)}\"";

        builder.ToTable(tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "CK_PlayerTags_LifecycleArchiveMetadata",
                $"({lifecycleStatusColumn} = {(int)LifecycleStatus.Active} AND {archivedAtColumn} IS NULL AND {archivedByIdColumn} IS NULL) OR "
                + $"({lifecycleStatusColumn} = {(int)LifecycleStatus.Archived} AND {archivedAtColumn} IS NOT NULL AND {archivedByIdColumn} IS NOT NULL)"));

        builder
            .HasOne(e => e.Club)
            .WithMany(c => c.PlayerTags)
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
