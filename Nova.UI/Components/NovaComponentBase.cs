using Microsoft.AspNetCore.Components;

namespace Nova.UI.Components;

/// <summary>
/// Provides a common base class for Nova components with cooperative cancellation and async disposal support.
/// </summary>
public abstract class NovaComponentBase : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// Represents a canceled token returned when the component has already been disposed.
    /// </summary>
    private static readonly CancellationToken DisposedCancellationToken = new(canceled: true);

    /// <summary>
    /// Stores the component-scoped cancellation token source and is created only when first needed.
    /// </summary>
    private CancellationTokenSource? componentCancellationTokenSource;

    /// <summary>
    /// Indicates whether asynchronous disposal has started for this component instance.
    /// </summary>
    private bool isDisposed;

    /// <summary>
    /// Gets a cancellation token that is active while the component is alive.
    /// </summary>
    /// <remarks>
    /// If the component has already been disposed, this returns an already-canceled token instead of throwing.
    /// </remarks>
    protected CancellationToken ComponentCancellationToken
    {
        get
        {
            if (isDisposed)
            {
                return DisposedCancellationToken;
            }

            componentCancellationTokenSource ??= new CancellationTokenSource();
            return componentCancellationTokenSource.Token;
        }
    }

    /// <summary>
    /// Disposes the component asynchronously.
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous disposal operation.
    /// </returns>
    public async ValueTask DisposeAsync()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        if (componentCancellationTokenSource is not null)
        {
            componentCancellationTokenSource.Cancel();
            componentCancellationTokenSource.Dispose();

            componentCancellationTokenSource = null;
        }

        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// When overridden in a derived type, performs custom asynchronous cleanup.
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous cleanup operation.
    /// </returns>
    protected virtual ValueTask DisposeAsyncCore()
    {
        return ValueTask.CompletedTask;
    }
}
