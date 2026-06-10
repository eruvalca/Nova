using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Shared.Security;

namespace Nova.Data.Startup;

/// <summary>
/// Performs startup database initialization tasks.
/// </summary>
internal static class StartupDatabaseInitializer
{
    /// <summary>
    /// Applies startup database initialization for migrations and role seeding.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <param name="applyMigrations">
    /// A value indicating whether pending EF Core migrations should be applied.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when initialization is finished.</returns>
    public static async Task InitializeAsync(
        IServiceProvider serviceProvider,
        bool applyMigrations,
        CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var services = scope.ServiceProvider;

        // Migrations are attributed to NovaDbContext, so it must be the context that applies them.
        var context = services.GetRequiredService<NovaDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<long>>>();

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            if (applyMigrations)
            {
                await context.Database.MigrateAsync(cancellationToken);
            }

            await EnsureRolesExistAsync(roleManager);
        });
    }

    /// <summary>
    /// Ensures all required application roles exist.
    /// </summary>
    /// <param name="roleManager">The role manager used to create roles.</param>
    /// <returns>A task that completes when all required roles are present.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a required role cannot be created.</exception>
    private static async Task EnsureRolesExistAsync(RoleManager<IdentityRole<long>> roleManager)
    {
        string[] roles = [Roles.Admin, Roles.ClubAdmin, Roles.StandardUser];

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole<long>(role));
                if (!roleResult.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to create role '{role}': {string.Join("; ", roleResult.Errors.Select(e => e.Description))}");
                }
            }
        }
    }
}
