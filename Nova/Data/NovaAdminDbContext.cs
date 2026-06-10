using Microsoft.EntityFrameworkCore;
using Nova.Data.Tenancy;

namespace Nova.Data;

/// <summary>
/// An unfiltered context for platform administration and ASP.NET Core Identity stores.
/// Tenant query filters are bypassed: ALL clubs' data is visible. Only register or inject
/// this context in Admin-gated features and Identity infrastructure.
/// </summary>
/// <param name="options">The context options.</param>
/// <param name="currentUser">The current user provider.</param>
public class NovaAdminDbContext(DbContextOptions<NovaAdminDbContext> options, ICurrentUserProvider currentUser)
    : ApplicationDbContext(options, currentUser, bypassTenantFilter: true)
{
}
