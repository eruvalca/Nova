using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Security;

namespace Nova.UI.Features.Clubs.Components;

/// <summary>Provides the role-shaped local directory and working hall shared by Club routes.</summary>
public partial class ClubShell(AuthenticationStateProvider authenticationStateProvider)
{
    private bool _isClubAdmin;

    [Parameter, EditorRequired]
    public required string CurrentLabel { get; set; }

    [Parameter, EditorRequired]
    public required RenderFragment ChildContent { get; set; }

    protected override void OnInitialized()
        => authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;

    protected override async Task OnInitializedAsync()
        => _isClubAdmin = (await authenticationStateProvider.GetAuthenticationStateAsync()).User.IsInRole(Roles.ClubAdmin);

    private void OnAuthenticationStateChanged(Task<AuthenticationState> stateTask)
        => _ = InvokeAsync(() => ApplyAuthenticationStateAsync(stateTask));

    private async Task ApplyAuthenticationStateAsync(Task<AuthenticationState> stateTask)
    {
        var isClubAdmin = (await stateTask).User.IsInRole(Roles.ClubAdmin);
        if (isClubAdmin != _isClubAdmin)
        {
            _isClubAdmin = isClubAdmin;
            await InvokeAsync(StateHasChanged);
        }
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        await base.DisposeAsyncCore();
    }
}
