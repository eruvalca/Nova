using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Nova.Entities;

namespace Nova.Unit.Tests.Data;

/// <summary>Maps ordered campaign closure and placement receipt instants to UTC ticks in the SQLite service harness.</summary>
/// <param name="dependencies">The default model customization dependencies.</param>
#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
internal sealed class CampaignClosureSqliteModelCustomizer(ModelCustomizerDependencies dependencies)
#pragma warning restore CA1812
    : ModelCustomizer(dependencies)
{
    /// <summary>Preserves the application model while replacing SQLite's unsupported timestamp ordering.</summary>
    /// <param name="modelBuilder">The model under construction.</param>
    /// <param name="context">The SQLite test context.</param>
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<CampaignEntity>().Property(campaign => campaign.ClosedAt)
            .HasConversion(value => value.HasValue ? value.Value.UtcTicks : (long?)null,
                value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);
        modelBuilder.Entity<PlacementMutationReceiptEntity>().Property(receipt => receipt.RecoveryExpiresAt)
            .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
    }
}
