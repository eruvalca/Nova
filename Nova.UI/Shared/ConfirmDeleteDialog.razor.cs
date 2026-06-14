using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Nova.UI.Components;

namespace Nova.UI.Shared;

/// <summary>
/// Bootstrap 5 modal that gates deletion of the current user's club and account behind an explicit confirmation checkbox.
/// Rendered with <c>InteractiveAuto</c> at the call site so the checkbox interaction works in WASM/Server.
/// </summary>
public partial class ConfirmDeleteDialog(IJSRuntime jsRuntime) : NovaComponentBase
{
    /// <summary>Gets or sets the name of the club that will be deleted. Displayed in the warning text.</summary>
    [Parameter, EditorRequired]
    public string ClubName { get; set; } = string.Empty;

    /// <summary>Gets or sets the HTML <c>id</c> of the form the confirm button will submit.</summary>
    [Parameter, EditorRequired]
    public string FormId { get; set; } = string.Empty;

    private bool _confirmed;

    /// <summary>Handles the confirm checkbox change event, enabling or disabling the submit button.</summary>
    /// <param name="e">The change event arguments.</param>
    private void OnConfirmChanged(ChangeEventArgs e) =>
        _confirmed = e.Value is true;

    /// <summary>Shows the Bootstrap modal by invoking the <c>novaShowModal</c> JavaScript helper.</summary>
    /// <returns>A task that completes when the JS call returns.</returns>
    public async Task ShowAsync() =>
        await jsRuntime.InvokeVoidAsync("novaShowModal", "#confirm-delete-modal");
}
