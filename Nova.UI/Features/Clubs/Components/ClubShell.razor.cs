using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Nova.Shared.Security;

namespace Nova.UI.Features.Clubs.Components;

/// <summary>
/// Provides the role-shaped local directory and working hall shared by Club routes.
/// </summary>
/// <param name="authenticationStateProvider">The provider used to read the current principal's ClubAdmin role and to track login/logout changes.</param>
/// <param name="jsRuntime">The runtime used to restore navigation focus after interactive attachment.</param>
public partial class ClubShell(AuthenticationStateProvider authenticationStateProvider, IJSRuntime jsRuntime)
{
    /// <summary>The current shell's working hall, which contains the destination heading.</summary>
    private ElementReference _hall;

    /// <summary>Whether a destination heading still needs focus after asynchronous content appears.</summary>
    private bool _headingFocusPending = true;

    /// <summary>The lazily imported module that restores focus lost when prerendered content is replaced.</summary>
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask = new(() => jsRuntime
        .InvokeAsync<IJSObjectReference>("import", "./_content/Nova.UI/Features/Clubs/Components/ClubShell.razor.js")
        .AsTask());

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_headingFocusPending)
        {
            var module = await _moduleTask.Value;
            _headingFocusPending = !await module.InvokeAsync<bool>("restoreHeadingFocusAfterAttach", ComponentCancellationToken, _hall);
        }
    }

    /// <summary>
    /// Whether the current principal holds the ClubAdmin role.
    /// </summary>
    private bool _isClubAdmin;

    /// <summary>
    /// Whether the club directory sheet is open. The shell owns this state in Blazor so
    /// re-renders cannot snap the sheet closed, as Bootstrap's class-based collapse did.
    /// </summary>
    private bool _isDirectoryOpen;

    /// <summary>
    /// The label rendered for the current club route.
    /// </summary>
    [Parameter, EditorRequired]
    public required string CurrentLabel { get; set; }

    /// <summary>
    /// The route content to render inside the shell.
    /// </summary>
    [Parameter, EditorRequired]
    public required RenderFragment ChildContent { get; set; }

    /// <summary>
    /// Toggles the club directory sheet between its collapsed and expanded states.
    /// </summary>
    private void ToggleDirectory() => _isDirectoryOpen = !_isDirectoryOpen;

    /// <summary>Closes the mobile sheet when a directory link is activated, including reused page instances.</summary>
    private void CloseDirectory() => _isDirectoryOpen = false;

    /// <inheritdoc />
    protected override void OnInitialized()
        => authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
        => _isClubAdmin = (await authenticationStateProvider.GetAuthenticationStateAsync()).User.IsInRole(Roles.ClubAdmin);

    /// <summary>
    /// Marshals an authentication-state change onto the renderer dispatcher so the
    /// reconciliation is serialized with renders and other UI callbacks.
    /// </summary>
    private void OnAuthenticationStateChanged(Task<AuthenticationState> stateTask)
        => _ = InvokeAsync(() => ApplyAuthenticationStateAsync(stateTask));

    /// <summary>
    /// Re-evaluates the ClubAdmin role from the new principal and re-renders only when it changed.
    /// </summary>
    private async Task ApplyAuthenticationStateAsync(Task<AuthenticationState> stateTask)
    {
        var isClubAdmin = (await stateTask).User.IsInRole(Roles.ClubAdmin);
        if (isClubAdmin != _isClubAdmin)
        {
            _isClubAdmin = isClubAdmin;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        if (_moduleTask.IsValueCreated)
        {
            try
            {
                var module = await _moduleTask.Value;
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // A disconnected circuit has already released the browser's component state.
            }
        }
        await base.DisposeAsyncCore();
    }
}
