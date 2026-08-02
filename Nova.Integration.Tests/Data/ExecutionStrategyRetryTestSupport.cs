using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nova.Data;
using Nova.Data.Interceptors;
using Nova.Data.Tenancy;
using Npgsql;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Creates retry-enabled tenant contexts while tracking how many execution attempts requested a
/// context from the service under test.
/// </summary>
internal sealed class RetryingTenantDbContextFactory(
    string connectionString,
    ICurrentUserProvider currentUser,
    IInterceptor transientFailureInterceptor) : IDbContextFactory<NovaDbContext>
{
    private int _createdContextCount;

    /// <summary>
    /// Gets the number of contexts created for execution-strategy setup and mutation attempts.
    /// </summary>
    public int CreatedContextCount => Volatile.Read(ref _createdContextCount);

    /// <inheritdoc />
    public NovaDbContext CreateDbContext() => CreateContext();

    /// <inheritdoc />
    public ValueTask<NovaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(CreateContext());

    /// <summary>
    /// Creates one retry-enabled tenant context with the transient-failure interceptor attached.
    /// </summary>
    /// <returns>A new tenant context owned by the caller.</returns>
    private NovaDbContext CreateContext()
    {
        Interlocked.Increment(ref _createdContextCount);

        var options = new DbContextOptionsBuilder<NovaDbContext>()
            .UseNpgsql(
                connectionString,
                providerOptions => providerOptions.EnableRetryOnFailure(
                    maxRetryCount: 1,
                    maxRetryDelay: TimeSpan.Zero,
                    errorCodesToAdd: null))
            .UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance)
            .AddInterceptors(new TenantSaveChangesInterceptor(), transientFailureInterceptor)
            .Options;

        return new NovaDbContext(options, currentUser);
    }

}

/// <summary>
/// Simulates one transient failure after the database has committed a transaction but before the
/// application receives a successful commit result.
/// </summary>
internal sealed class FailFirstCommittedTransactionInterceptor : DbTransactionInterceptor
{
    private int _shouldFail = 1;
    private int _failureCount;

    /// <summary>
    /// Gets the number of ambiguous commit failures injected by this interceptor.
    /// </summary>
    public int FailureCount => Volatile.Read(ref _failureCount);

    /// <inheritdoc />
    public override Task TransactionCommittedAsync(
        System.Data.Common.DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _shouldFail, 0) == 1)
        {
            Interlocked.Increment(ref _failureCount);
            throw new NpgsqlException("Simulated ambiguous commit failure.", new TimeoutException());
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Simulates one transient provider failure while a mutation attempt is still reading, before it
/// has written or committed anything.
/// </summary>
internal sealed class FailFirstTeamReadInterceptor : DbCommandInterceptor
{
    private int _shouldFail = 1;
    private int _failureCount;

    /// <summary>
    /// Gets the number of transient read failures injected by this interceptor.
    /// </summary>
    public int FailureCount => Volatile.Read(ref _failureCount);

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
        System.Data.Common.DbCommand command,
        CommandEventData eventData,
        InterceptionResult<System.Data.Common.DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FROM \"Teams\"", StringComparison.Ordinal)
            && Interlocked.Exchange(ref _shouldFail, 0) == 1)
        {
            Interlocked.Increment(ref _failureCount);
            throw new NpgsqlException("Simulated transient read failure.", new TimeoutException());
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}

internal sealed class FailFirstSaveChangesInterceptor : SaveChangesInterceptor
{
    private int _shouldFail = 1;
    private int _failureCount;

    /// <summary>
    /// Gets the number of transient failures injected by this interceptor.
    /// </summary>
    public int FailureCount => Volatile.Read(ref _failureCount);

    /// <inheritdoc />
    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _shouldFail, 0) == 1)
        {
            Interlocked.Increment(ref _failureCount);
            throw new NpgsqlException("Simulated transient save failure.", new TimeoutException());
        }

        return ValueTask.FromResult(result);
    }
}
