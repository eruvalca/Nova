using Microsoft.EntityFrameworkCore;

namespace Nova.Unit.Tests.Account;

/// <summary>
/// A simple test implementation of IDbContextFactory for testing services that depend on it.
/// </summary>
/// <typeparam name="TContext">The DbContext type to create.</typeparam>
public sealed class TestDbContextFactory<TContext> : IDbContextFactory<TContext> where TContext : DbContext
{
    /// <summary>
    /// The delegate that creates new instances of the DbContext.
    /// </summary>
    private readonly Func<TContext> _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="TestDbContextFactory{TContext}"/> class.
    /// </summary>
    /// <param name="factory">A delegate that creates a new instance of <typeparamref name="TContext"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    public TestDbContextFactory(Func<TContext> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Creates a new instance of the DbContext.
    /// </summary>
    /// <returns>A new instance of <typeparamref name="TContext"/>.</returns>
    public TContext CreateDbContext() => _factory();

    /// <summary>
    /// Creates a new instance of the DbContext asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is a new instance of <typeparamref name="TContext"/>.</returns>
    public ValueTask<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(_factory());
}
