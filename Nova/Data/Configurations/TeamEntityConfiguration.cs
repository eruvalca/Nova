using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;
using Nova.Shared.Enums;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Team Entity Configuration.
/// </summary>
public class TeamEntityConfiguration : IEntityTypeConfiguration<TeamEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<TeamEntity> builder)
    {
        builder.HasKey(e => e.TeamId);
        builder.Property(e => e.TeamId)
            .ValueGeneratedOnAdd();
        builder.Property(e => e.LifecycleStatus)
            .IsConcurrencyToken();

        var lifecycleStatusColumn = $"\"{nameof(TeamEntity.LifecycleStatus)}\"";
        var archivedAtColumn = $"\"{nameof(TeamEntity.ArchivedAt)}\"";
        var archivedByIdColumn = $"\"{nameof(TeamEntity.ArchivedById)}\"";

        builder.ToTable(tableBuilder =>
            tableBuilder.HasCheckConstraint(
                "CK_Teams_LifecycleArchiveMetadata",
                $"({lifecycleStatusColumn} = {(int)LifecycleStatus.Active} AND {archivedAtColumn} IS NULL AND {archivedByIdColumn} IS NULL) OR "
                + $"({lifecycleStatusColumn} = {(int)LifecycleStatus.Archived} AND {archivedAtColumn} IS NOT NULL AND {archivedByIdColumn} IS NOT NULL)"));

        builder
            .HasOne(e => e.Club)
            .WithMany(c => c.Teams)
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
