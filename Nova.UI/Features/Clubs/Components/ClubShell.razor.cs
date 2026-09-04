using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Security;

namespace Nova.UI.Features.Clubs.Components;

/// <summary>
/// Provides the role-shaped local directory and working hall shared by Club routes.
/// </summary>
/// <param name="authenticationStateProvider">The provider used to read the current principal's ClubAdmin role and to track login/logout changes.</param>
public partial class ClubShell(AuthenticationStateProvider authenticationStateProvider)
{
    /// <summary>
    /// Whether the current principal holds the ClubAdmin role.
    /// </summary>
    private bool _isClubAdmin;

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
        await base.DisposeAsyncCore();
    }
}
