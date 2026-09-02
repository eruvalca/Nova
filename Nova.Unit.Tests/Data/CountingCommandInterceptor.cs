using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Nova.Unit.Tests.Data;

/// <summary>Counts asynchronous relational reader commands issued by a test context.</summary>
internal sealed class CountingCommandInterceptor : DbCommandInterceptor
{
    /// <summary>Gets the number of reader commands observed.</summary>
    public int ReaderExecutionCount { get; private set; }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ReaderExecutionCount++;
        return ValueTask.FromResult(result);
    }
}
