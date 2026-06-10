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

        builder
            .HasMany(c => c.Seasons)
            .WithOne(s => s.Club)
            .HasForeignKey(s => s.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(c => c.Campaigns)
            .WithOne(ca => ca.Club)
            .HasForeignKey(ca => ca.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(c => c.Teams)
            .WithOne(t => t.Club)
            .HasForeignKey(t => t.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(c => c.Players)
            .WithOne(p => p.Club)
            .HasForeignKey(p => p.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(c => c.PlayerTags)
            .WithOne(pt => pt.Club)
            .HasForeignKey(pt => pt.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany<NoteEntity>()
            .WithOne(n => n.Club)
            .HasForeignKey(n => n.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(c => c.JoinRequests)
            .WithOne(r => r.Club)
            .HasForeignKey(r => r.ClubId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
