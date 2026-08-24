using Cropper.Blazor.Components;
using Cropper.Blazor.Events;
using Cropper.Blazor.Events.CropReadyEvent;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Nova.UI.Shared;

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
    /// Invoked when the underlying Cropper.js instance has finished loading the image and is
    /// ready to accept <c>getCroppedCanvas</c> calls. Before this event fires, an export would
    /// fail because the JS <c>cropperInstances</c> entry does not exist yet, so hosts gate
    /// their "save" action on this signal.
    /// </summary>
    [Parameter]
    public EventCallback OnReady { get; set; }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        base.OnInitialized();

        // The library exposes the ready signal as a settable [Parameter] action; hook it here so
        // consumers do not have to know about the library's JS interop event chain.
        OnReadyEvent = HandleReady;
    }

    /// <summary>
    /// Simulates the library's ready event. Test-only escape hatch: bUnit cannot drive the JS
    /// module that fires <c>IsReady</c>, so component tests call this to unblock save actions.
    /// </summary>
    internal void SimulateReady() => HandleReady(new JSEventData<CropReadyEvent>());

    /// <summary>
    /// Handles the library's ready event: marks the cropper ready and notifies the consumer.
    /// </summary>
    /// <param name="eventData">The ready event metadata supplied by Cropper.Blazor.</param>
    private void HandleReady(JSEventData<CropReadyEvent> eventData)
    {
        _ = OnReady.InvokeAsync();
    }

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
