using Microsoft.EntityFrameworkCore;
using Nova.Data.Tenancy;

namespace Nova.Data;

/// <summary>
/// A read-only, tenant-scoped context for efficient bulk reads. Queries are not tracked
/// and any attempt to save changes throws.
/// </summary>
public class NovaReadDbContext : ApplicationDbContext
{
    private const string ReadOnlyMessage = $"{nameof(NovaReadDbContext)} is read-only. Use {nameof(NovaDbContext)} for writes.";

    /// <summary>
    /// Initializes a new instance of the <see cref="NovaReadDbContext"/> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    /// <param name="currentUser">The current user provider.</param>
    public NovaReadDbContext(DbContextOptions<NovaReadDbContext> options, ICurrentUserProvider currentUser)
        : base(options, currentUser, bypassTenantFilter: false)
    {
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        ChangeTracker.AutoDetectChangesEnabled = false;
    }

    /// <inheritdoc />
    public override int SaveChanges() => throw new InvalidOperationException(ReadOnlyMessage);

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw new InvalidOperationException(ReadOnlyMessage);

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException(ReadOnlyMessage);

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => throw new InvalidOperationException(ReadOnlyMessage);
}
