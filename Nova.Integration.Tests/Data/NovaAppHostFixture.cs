using Aspire.Hosting;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Interceptors;
using Nova.Data.Tenancy;
using Nova.Shared.Security;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// A flow-scoped <see cref="ICurrentUserProvider"/> for simulating different users in tests.
/// </summary>
/// <remarks>
/// The property values are backed by <see cref="AsyncLocal{T}"/> so that mutations made inside one
/// test method flow across its awaits and child tasks but never leak into concurrently running
/// tests (xUnit v3 resets the execution context between test cases). Direct assignment is the
/// normal flow-local idiom; use <see cref="NovaAppHostFixture.UseUser"/> when restore-on-dispose
/// semantics are needed.
/// </remarks>
public sealed class FakeCurrentUserProvider : ICurrentUserProvider
{
    private readonly AsyncLocal<long?> _userId = new();
    private readonly AsyncLocal<long?> _clubId = new();
    private readonly AsyncLocal<bool> _isClubAdmin = new();

    /// <summary>Gets or sets the simulated user id, or <see langword="null"/> for anonymous.</summary>
    public long? UserId { get => _userId.Value; set => _userId.Value = value; }

    /// <summary>Gets or sets the simulated club id, or <see langword="null"/> when the user has no club.</summary>
    public long? ClubId { get => _clubId.Value; set => _clubId.Value = value; }

    /// <summary>Gets or sets a value indicating whether the simulated user is a club admin.</summary>
    public bool IsClubAdmin { get => _isClubAdmin.Value; set => _isClubAdmin.Value = value; }

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
/// connection string plus factories for the three application contexts wired to a flow-scoped
/// <see cref="FakeCurrentUserProvider"/>.
/// </summary>
public sealed class NovaAppHostFixture : IAsyncLifetime
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private DistributedApplication? App { get; set; }
    private string? ConnectionStringValue { get; set; }
    private BlobContainerClient? ProfilePhotosContainerValue { get; set; }

    /// <summary>Gets the mutable current-user provider used by all contexts created by this fixture.</summary>
    public FakeCurrentUserProvider CurrentUser { get; } = new();

    /// <summary>
    /// Sets the simulated current user for the remainder of the current test flow and restores the
    /// previous values when the returned scope is disposed. Because the provider is AsyncLocal-backed,
    /// the scope only affects the calling test — concurrent tests are unaffected.
    /// </summary>
    /// <param name="userId">The simulated user id, or <see langword="null"/> for anonymous.</param>
    /// <param name="clubId">The simulated club id, or <see langword="null"/> when the user has no club.</param>
    /// <param name="isClubAdmin">Whether the simulated user is a club admin.</param>
    /// <returns>A scope that restores the previous current-user values on disposal.</returns>
    public IDisposable UseUser(long? userId, long? clubId, bool isClubAdmin)
    {
        var previous = (CurrentUser.UserId, CurrentUser.ClubId, CurrentUser.IsClubAdmin);
        CurrentUser.UserId = userId;
        CurrentUser.ClubId = clubId;
        CurrentUser.IsClubAdmin = isClubAdmin;
        return new RestoreUserScope(CurrentUser, previous);
    }

    /// <summary>
    /// Restores a <see cref="FakeCurrentUserProvider"/> to a snapshot on disposal.
    /// </summary>
    /// <param name="provider">The provider to restore.</param>
    /// <param name="previous">The values captured before the scope was entered.</param>
    private sealed class RestoreUserScope(
        FakeCurrentUserProvider provider,
        (long? UserId, long? ClubId, bool IsClubAdmin) previous) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose()
        {
            provider.UserId = previous.UserId;
            provider.ClubId = previous.ClubId;
            provider.IsClubAdmin = previous.IsClubAdmin;
        }
    }

    /// <summary>
    /// A dedicated, never-mutated provider used only by admin contexts. Admin contexts bypass
    /// the tenant filter, and seeding always sets audit fields explicitly, so the interceptor
    /// must never stamp actors from the shared <see cref="CurrentUser"/> state that other test
    /// classes mutate and may leave populated.
    /// </summary>
    private static readonly FakeCurrentUserProvider AdminContextUser = new();

    /// <summary>Gets the connection string for the live "novadb" PostgreSQL database.</summary>
    public string ConnectionString => ConnectionStringValue
        ?? throw new InvalidOperationException("The AppHost has not been started.");

    /// <summary>Gets the blob container client for the live "profile-photos" container (Azurite emulator).</summary>
    public BlobContainerClient ProfilePhotosContainer => ProfilePhotosContainerValue
        ?? throw new InvalidOperationException("The AppHost has not been started.");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        using var cts = new CancellationTokenSource(StartupTimeout);
        var cancellationToken = cts.Token;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Nova_AppHost>(cancellationToken);

        RemoveDataVolumes(builder);

        App = await builder.BuildAsync(cancellationToken);
        await App.StartAsync(cancellationToken);

        // Healthy means the app is serving and the database is reachable.
        await App.ResourceNotifications.WaitForResourceHealthyAsync("nova", cancellationToken);

        ConnectionStringValue = await App.GetConnectionStringAsync("novadb", cancellationToken)
            ?? throw new InvalidOperationException("No connection string was resolved for 'novadb'.");

        var blobConnectionString = await App.GetConnectionStringAsync("profile-photos", cancellationToken)
            ?? throw new InvalidOperationException("No connection string was resolved for 'profile-photos'.");
        ProfilePhotosContainerValue = CreateBlobContainerClient(blobConnectionString);
        await ProfilePhotosContainerValue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        // The app only migrates at startup in the Development environment, which the testing
        // builder does not guarantee — apply the production migrations explicitly. Migrations
        // are attributed to NovaDbContext, so they must be applied through it.
        await using var context = CreateTenantContext();
        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
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
        new(Options<NovaAdminDbContext>(withInterceptor: true), AdminContextUser);

    /// <summary>
    /// Gets the base URI of the running "nova" web resource, preferring the https endpoint so
    /// <c>UseHttpsRedirection</c> does not turn browser navigations into redirects.
    /// </summary>
    public Uri NovaBaseUri
    {
        get
        {
            var app = App ?? throw new InvalidOperationException("The AppHost has not been started.");

            // Prefer the https endpoint so UseHttpsRedirection does not turn every request into a 307.
            try
            {
                return app.GetEndpoint("nova", "https");
            }
            catch (ArgumentException)
            {
                return app.GetEndpoint("nova", "http");
            }
        }
    }

    /// <summary>
    /// Creates an <see cref="IDbContextFactory{NovaDbContext}"/> wired to the live database with
    /// the tenant interceptor and the shared mutable <see cref="CurrentUser"/> provider.
    /// Browser tests use it to drive server-side services (for example campaign close) that have
    /// no HTTP or UI surface yet.
    /// </summary>
    /// <returns>A tenant-context factory.</returns>
    public IDbContextFactory<NovaDbContext> CreateTenantContextFactory() =>
        new TenantContextFactory(Options<NovaDbContext>(withInterceptor: true), CurrentUser);

    /// <summary>
    /// A plain context factory that constructs <see cref="NovaDbContext"/> instances with the
    /// shared mutable current-user provider, mirroring <see cref="CreateTenantContext"/>.
    /// </summary>
    /// <param name="options">The live-database options.</param>
    /// <param name="currentUserProvider">The mutable current-user provider.</param>
    private sealed class TenantContextFactory(
        DbContextOptions<NovaDbContext> options,
        ICurrentUserProvider currentUserProvider) : IDbContextFactory<NovaDbContext>
    {
        /// <inheritdoc />
        public NovaDbContext CreateDbContext() => new(options, currentUserProvider);
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> targeting the running "nova" web resource, with a
    /// per-client cookie container (Identity + antiforgery cookies) and redirect-following
    /// disabled by default so tests can assert on status codes and Location headers.
    /// </summary>
    /// <param name="allowAutoRedirect">Whether the client should follow redirects automatically.</param>
    /// <returns>A new client owned by the caller.</returns>
    public HttpClient CreateNovaHttpClient(bool allowAutoRedirect = false)
    {
        var baseAddress = NovaBaseUri;

        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = allowAutoRedirect,
            // The Aspire-launched app serves the untrusted ASP.NET Core dev certificate.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };

        return new HttpClient(handler) { BaseAddress = baseAddress };
    }

    /// <summary>
    /// Builds a <see cref="BlobContainerClient"/> from the Aspire container resource connection
    /// string, which appends <c>ContainerName=...</c> to the Azurite service connection string.
    /// </summary>
    /// <param name="connectionString">The container resource connection string.</param>
    /// <returns>The container client.</returns>
    private static BlobContainerClient CreateBlobContainerClient(string connectionString)
    {
        var segments = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var containerSegment = segments.FirstOrDefault(s => s.StartsWith("ContainerName=", StringComparison.OrdinalIgnoreCase));
        var containerName = containerSegment?["ContainerName=".Length..] ?? "profile-photos";
        if (containerSegment is not null)
        {
            segments.Remove(containerSegment);
        }

        return new BlobServiceClient(string.Join(';', segments)).GetBlobContainerClient(containerName);
    }

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
