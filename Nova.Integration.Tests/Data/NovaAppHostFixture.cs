using Aspire.Hosting;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Interceptors;
using Nova.Data.Tenancy;
using Nova.Shared.Security;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// A mutable <see cref="ICurrentUserProvider"/> for simulating different users in tests.
/// </summary>
public sealed class FakeCurrentUserProvider : ICurrentUserProvider
{
    /// <summary>Gets or sets the simulated user id, or <see langword="null"/> for anonymous.</summary>
    public long? UserId { get; set; }

    /// <summary>Gets or sets the simulated club id, or <see langword="null"/> when the user has no club.</summary>
    public long? ClubId { get; set; }

    /// <summary>Gets or sets a value indicating whether the simulated user is a club admin.</summary>
    public bool IsClubAdmin { get; set; }

    /// <summary>
    /// Builds the <see cref="CurrentUserState"/> union from the current property values.
    /// </summary>
    /// <returns>The simulated user's state.</returns>
    public CurrentUserState GetCurrentUserState() =>
        (UserId, ClubId) switch
        {
            (null, _) => new Anonymous(),
            ({ } userId, null) => new AuthenticatedUser(userId),
            ({ } userId, { } clubId) => new ClubMember(userId, clubId, IsClubAdmin),
        };
}

/// <summary>
/// Starts the real Nova AppHost (PostgreSQL 18 container plus the Nova web app) once per test
/// collection, applies the production EF Core migrations, and exposes the live "novadb"
/// connection string plus factories for the three application contexts wired to a mutable
/// <see cref="FakeCurrentUserProvider"/>.
/// </summary>
public sealed class NovaAppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? _app;
    private string? _connectionString;

    /// <summary>Gets the mutable current-user provider used by all contexts created by this fixture.</summary>
    public FakeCurrentUserProvider CurrentUser { get; } = new();

    /// <summary>Gets the connection string for the live "novadb" PostgreSQL database.</summary>
    public string ConnectionString => _connectionString
        ?? throw new InvalidOperationException("The AppHost has not been started.");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);
        var cancellationToken = cts.Token;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Nova_AppHost>(cancellationToken);

        RemoveDataVolumes(builder);

        _app = await builder.BuildAsync(cancellationToken);
        await _app.StartAsync(cancellationToken);

        // Healthy means the app is serving and the database is reachable.
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("nova", cancellationToken);

        _connectionString = await _app.GetConnectionStringAsync("novadb", cancellationToken)
            ?? throw new InvalidOperationException("No connection string was resolved for 'novadb'.");

        // The app only migrates at startup in the Development environment, which the testing
        // builder does not guarantee — apply the production migrations explicitly. Migrations
        // are attributed to NovaDbContext, so they must be applied through it.
        await using var context = CreateTenantContext();
        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates a tenant-scoped <see cref="NovaDbContext"/> (filters and interceptor on) against the live database.
    /// </summary>
    /// <returns>A new tenant context owned by the caller.</returns>
    public NovaDbContext CreateTenantContext() =>
        new(Options<NovaDbContext>(withInterceptor: true), CurrentUser);

    /// <summary>
    /// Creates a read-only <see cref="NovaReadDbContext"/> (filters on, no tracking) against the live database.
    /// </summary>
    /// <returns>A new read context owned by the caller.</returns>
    public NovaReadDbContext CreateReadContext() =>
        new(Options<NovaReadDbContext>(withInterceptor: false), CurrentUser);

    /// <summary>
    /// Creates an unfiltered <see cref="NovaAdminDbContext"/> (interceptor on, filters bypassed) against the live database.
    /// </summary>
    /// <returns>A new admin context owned by the caller.</returns>
    public NovaAdminDbContext CreateAdminContext() =>
        new(Options<NovaAdminDbContext>(withInterceptor: true), CurrentUser);

    /// <summary>
    /// Strips data-volume mounts from container resources so test runs always start from an empty
    /// database instead of reusing the developer's persistent volume from the AppHost model.
    /// </summary>
    /// <param name="builder">The testing builder whose resources are adjusted.</param>
    private static void RemoveDataVolumes(IDistributedApplicationTestingBuilder builder)
    {
        foreach (var resource in builder.Resources)
        {
            var volumes = resource.Annotations
                .OfType<ContainerMountAnnotation>()
                .Where(mount => mount.Type == ContainerMountType.Volume)
                .ToList();

            foreach (var volume in volumes)
            {
                resource.Annotations.Remove(volume);
            }
        }
    }

    /// <summary>
    /// Builds Npgsql-backed options for the requested context type, mirroring the production
    /// provider configuration in <c>Nova/Program.cs</c>.
    /// </summary>
    /// <typeparam name="TContext">The context type the options are for.</typeparam>
    /// <param name="withInterceptor">Whether to attach the <see cref="TenantSaveChangesInterceptor"/>.</param>
    /// <returns>The configured options.</returns>
    private DbContextOptions<TContext> Options<TContext>(bool withInterceptor) where TContext : DbContext
    {
        // Attach the pinned Identity options so the model matches the running app (and the migrations).
        var builder = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(ConnectionString)
            .UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance);
        if (withInterceptor)
        {
            builder.AddInterceptors(new TenantSaveChangesInterceptor());
        }

        return builder.Options;
    }
}

/// <summary>
/// Collection definition that shares a single running AppHost across all Postgres-backed tests,
/// paying the container startup cost once.
/// </summary>
[CollectionDefinition(Name)]
public sealed class NovaAppHostCollection : ICollectionFixture<NovaAppHostFixture>
{
    /// <summary>The collection name used by tests that need the live AppHost.</summary>
    public const string Name = "NovaAppHost";
}
