using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Nova.Data.Tenancy;

namespace Nova.Data;

/// <summary>
/// Design-time factory so `dotnet ef` commands can create <see cref="NovaDbContext"/>
/// without the Aspire host or an authenticated user.
/// </summary>
public sealed class NovaDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NovaDbContext>
{
    /// <summary>
    /// Executes the Create Db Context operation.
    /// </summary>
    /// <param name="args">The args.</param>
    /// <returns>The operation result.</returns>
    public NovaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NovaDbContext>()
            .UseNpgsql("Host=localhost;Database=novadb;Username=postgres")
            .UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance)
            .Options;

        return new NovaDbContext(options, new NullCurrentUserProvider());
    }
}
