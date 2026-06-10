using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nova.Entities;

namespace Nova.Data.Configurations;

/// <summary>
/// Configures EF Core mapping for Club Join Request Entity Configuration.
/// </summary>
public class ClubJoinRequestEntityConfiguration : IEntityTypeConfiguration<ClubJoinRequestEntity>
{
    /// <summary>
    /// Executes the Configure operation.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public void Configure(EntityTypeBuilder<ClubJoinRequestEntity> builder)
    {
        builder.HasKey(e => e.ClubJoinRequestId);
        builder.Property(e => e.ClubJoinRequestId)
            .ValueGeneratedOnAdd();

        builder
            .HasOne(e => e.Club)
            .WithMany(c => c.JoinRequests)
            .HasForeignKey(e => e.ClubId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(e => e.RequestingUser)
            .WithOne(u => u.SentJoinRequest)
            .HasForeignKey<ClubJoinRequestEntity>(e => e.RequestingUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
