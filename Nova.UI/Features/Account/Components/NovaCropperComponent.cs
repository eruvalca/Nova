using Cropper.Blazor.Components;
using Microsoft.JSInterop;

namespace Nova.UI.Features.Account.Components;

/// <summary>
/// A <see cref="CropperComponent"/> that tolerates disposal after the circuit has disconnected.
/// Cropper.Blazor (1.5.1) issues a JS destroy call from <see cref="CropperComponent.DisposeAsync"/>
/// without catching <see cref="JSDisconnectedException"/>, so disposing the component during a
/// full-document navigation, refresh, or tab close surfaces as an unhandled circuit error.
/// The browser tears the cropper down with the document, so the failed destroy call is moot.
/// </summary>
public sealed class NovaCropperComponent : CropperComponent, IAsyncDisposable
{
    /// <summary>
    /// Disposes the cropper, swallowing the interop exception thrown when the circuit is no
    /// longer available to receive the JS destroy call.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        try
        {
            await DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone; the browser already destroyed the cropper with the page.
        }
    }
}
