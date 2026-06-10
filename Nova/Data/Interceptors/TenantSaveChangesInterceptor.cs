using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nova.Entities.Base;

namespace Nova.Data.Interceptors;

/// <summary>
/// Stamps audit fields on <see cref="BaseEntity"/> entries and guards against cross-tenant
/// writes on <see cref="ITenantOwnedEntity"/> entries. Tenant guarding is skipped for
/// contexts with the tenant filter bypassed (admin); audit stamping always applies.
/// </summary>
public sealed class TenantSaveChangesInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ProcessChanges(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ProcessChanges(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ProcessChanges(DbContext? context)
    {
        if (context is not ApplicationDbContext appContext)
        {
            return;
        }

        var currentUser = appContext.CurrentUser;
        var enforceTenant = !appContext.TenantFilterBypassed;
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in appContext.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (enforceTenant && entry.Entity is ITenantOwnedEntity tenantOwned)
            {
                GuardTenant(entry, tenantOwned, currentUser.ClubId);
            }

            if (entry.Entity is BaseEntity auditable)
            {
                StampAudit(entry, auditable, currentUser.UserId, now);
            }
        }
    }

    private static void GuardTenant(EntityEntry entry, ITenantOwnedEntity entity, long? tenantId)
    {
        switch (entry.State)
        {
            case EntityState.Added when entity.ClubId == default:
                entity.ClubId = tenantId
                    ?? throw new InvalidOperationException(
                        $"Cannot save '{entry.Metadata.ClrType.Name}': the current user has no club.");
                break;
            case EntityState.Added or EntityState.Modified or EntityState.Deleted when entity.ClubId != tenantId:
                throw new InvalidOperationException(
                    $"Cross-tenant write detected for '{entry.Metadata.ClrType.Name}': entity ClubId {entity.ClubId} does not match current tenant {tenantId?.ToString() ?? "(none)"}.");
        }
    }

    private static void StampAudit(EntityEntry entry, BaseEntity entity, long? userId, DateTimeOffset now)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                entity.CreatedAt = now;
                if (userId.HasValue)
                {
                    entity.CreatedById = userId.Value;
                }

                break;
            case EntityState.Modified:
                entity.ModifiedAt = now;
                entity.ModifiedById = userId ?? entity.ModifiedById;
                break;
        }
    }
}
