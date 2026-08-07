using System.Data.Common;
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

/// <summary>
/// Simulates a non-transient failure immediately before the second save in one context so tests can
/// verify that earlier writes in the transaction are rolled back.
/// </summary>
internal sealed class FailSecondSaveChangesInterceptor : SaveChangesInterceptor
{
    private int _saveCount;
    private int _failureCount;

    /// <summary>
    /// Gets the number of rollback failures injected by this interceptor.
    /// </summary>
    public int FailureCount => Volatile.Read(ref _failureCount);

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref _saveCount) == 2)
        {
            Interlocked.Increment(ref _failureCount);
            throw new InvalidOperationException("Simulated campaign participation save failure.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

/// <summary>
/// Records PostgreSQL advisory-lock keys so tests can prove both sides of a shared invariant use the
/// same lock independently of task scheduling.
/// </summary>
internal sealed class AdvisoryLockRecordingInterceptor : DbCommandInterceptor
{
    private readonly List<long> _acquiredKeys = [];

    /// <summary>
    /// Gets a snapshot of the advisory-lock keys acquired so far.
    /// </summary>
    public IReadOnlyList<long> AcquiredKeys
    {
        get
        {
            lock (_acquiredKeys)
            {
                return [.. _acquiredKeys];
            }
        }
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Record(DbCommand command)
    {
        if (!command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))
        {
            return;
        }

        foreach (DbParameter parameter in command.Parameters)
        {
            if (parameter.Value is long key)
            {
                lock (_acquiredKeys)
                {
                    _acquiredKeys.Add(key);
                }
            }
        }
    }
}

/// <summary>
/// Pauses a mutation immediately after it acquires an advisory lock so a test can deterministically
/// queue a competing mutation behind it.
/// </summary>
internal sealed class AdvisoryLockGateInterceptor : DbCommandInterceptor
{
    private readonly TaskCompletionSource _attempted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _acquired =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Waits until the advisory-lock command is about to execute.</summary>
    public Task WaitForAttemptAsync(CancellationToken cancellationToken) =>
        _attempted.Task.WaitAsync(cancellationToken);

    /// <summary>Waits until the advisory lock has been acquired.</summary>
    public Task WaitForAcquiredAsync(CancellationToken cancellationToken) =>
        _acquired.Task.WaitAsync(cancellationToken);

    /// <summary>Allows the mutation holding the advisory lock to continue.</summary>
    public void Release() => _release.TrySetResult();

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        RecordAttempt(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (IsAdvisoryLock(command))
        {
            _acquired.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        return await base.NonQueryExecutedAsync(
            command,
            eventData,
            result,
            cancellationToken);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        RecordAttempt(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (IsAdvisoryLock(command))
        {
            _acquired.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        return await base.ReaderExecutedAsync(
            command,
            eventData,
            result,
            cancellationToken);
    }

    private void RecordAttempt(DbCommand command)
    {
        if (IsAdvisoryLock(command))
        {
            _attempted.TrySetResult();
        }
    }

    private static bool IsAdvisoryLock(DbCommand command) =>
        command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal);
}

/// <summary>
/// Commits an independent write immediately after the team duplicate-name probe runs, reproducing
/// the window in which another request inserts a conflicting team between the probe and the save.
/// </summary>
/// <param name="insertConflictAsync">The independent write to commit once, after the first probe.</param>
internal sealed class InsertAfterTeamExistsProbeInterceptor(Func<Task> insertConflictAsync) : DbCommandInterceptor
{
    private int _shouldInsert = 1;
    private int _insertCount;

    /// <summary>
    /// Gets the number of conflicting writes this interceptor committed.
    /// </summary>
    public int InsertCount => Volatile.Read(ref _insertCount);

    /// <inheritdoc />
    public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(
        System.Data.Common.DbCommand command,
        CommandExecutedEventData eventData,
        System.Data.Common.DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("EXISTS", StringComparison.Ordinal)
            && command.CommandText.Contains("\"Teams\"", StringComparison.Ordinal)
            && Interlocked.Exchange(ref _shouldInsert, 0) == 1)
        {
            await insertConflictAsync();
            Interlocked.Increment(ref _insertCount);
        }

        return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }
}

/// <summary>
/// Commits an independent placement immediately after a team update computes its player lock set,
/// reproducing the window in which another request places a player on the team being locked.
/// </summary>
/// <param name="insertPlacementAsync">The independent placement write to commit once.</param>
internal sealed class PlacementAfterLockSetInterceptor(Func<Task> insertPlacementAsync) : DbCommandInterceptor
{
    private int _shouldInsert = 1;
    private int _insertCount;

    /// <summary>
    /// Gets the number of placements this interceptor committed.
    /// </summary>
    public int InsertCount => Volatile.Read(ref _insertCount);

    /// <inheritdoc />
    public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(
        System.Data.Common.DbCommand command,
        CommandExecutedEventData eventData,
        System.Data.Common.DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        // The lock-set query is the only DISTINCT projection over placements the update issues.
        if (command.CommandText.Contains("DISTINCT", StringComparison.Ordinal)
            && command.CommandText.Contains("\"PlayerCampaignAssignments\"", StringComparison.Ordinal)
            && Interlocked.Exchange(ref _shouldInsert, 0) == 1)
        {
            await insertPlacementAsync();
            Interlocked.Increment(ref _insertCount);
        }

        return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }
}
