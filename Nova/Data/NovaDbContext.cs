using Microsoft.EntityFrameworkCore;
using Nova.Data.Tenancy;

namespace Nova.Data;

/// <summary>
/// The default tenant-scoped context. Query filters restrict data to the current user's club.
/// This is the migrations target.
/// </summary>
/// <param name="options">The context options.</param>
/// <param name="currentUser">The current user provider.</param>
public class NovaDbContext(DbContextOptions<NovaDbContext> options, ICurrentUserProvider currentUser)
    : ApplicationDbContext(options, currentUser, bypassTenantFilter: false)
{
}
