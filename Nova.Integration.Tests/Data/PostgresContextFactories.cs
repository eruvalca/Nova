using Microsoft.EntityFrameworkCore;
using Nova.Data;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Wraps the AppHost fixture's read-only context factory behind the <see cref="IDbContextFactory{TContext}"/>
/// contract so read-only query services can be constructed directly in provider tests.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
internal sealed class PostgresReadContextFactory(NovaAppHostFixture fixture) : IDbContextFactory<NovaReadDbContext>
{
    /// <inheritdoc />
    public NovaReadDbContext CreateDbContext() => fixture.CreateReadContext();

    /// <inheritdoc />
    public Task<NovaReadDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(fixture.CreateReadContext());
}

/// <summary>
/// Wraps the AppHost fixture's admin context factory behind the <see cref="IDbContextFactory{TContext}"/>
/// contract so services that mix admin and read contexts can be constructed directly in provider tests.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
internal sealed class PostgresAdminContextFactory(NovaAppHostFixture fixture) : IDbContextFactory<NovaAdminDbContext>
{
    /// <inheritdoc />
    public NovaAdminDbContext CreateDbContext() => fixture.CreateAdminContext();

    /// <inheritdoc />
    public Task<NovaAdminDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(fixture.CreateAdminContext());
}
