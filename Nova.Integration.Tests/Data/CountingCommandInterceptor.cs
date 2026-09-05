using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Nova.Integration.Tests.Data;

/// <summary>Records actual asynchronous provider readers, including batched write readers.</summary>
internal sealed class CountingCommandInterceptor : DbCommandInterceptor
{
    /// <summary>Retains statement text without parameter values for query-shape assertions.</summary>
    private readonly ConcurrentQueue<string> _readerCommands = new();

    /// <summary>Gets the number of asynchronous relational readers executed.</summary>
    public int ReaderExecutionCount => _readerCommands.Count;

    /// <summary>Gets query readers independently of provider INSERT RETURNING batching.</summary>
    public int SelectReaderExecutionCount => _readerCommands.Count(command => command.TrimStart().StartsWith("SELECT", StringComparison.Ordinal));

    /// <summary>Gets a snapshot of the asynchronous reader statements, without parameter values.</summary>
    public IReadOnlyList<string> ReaderCommands => _readerCommands.ToArray();

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _readerCommands.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
