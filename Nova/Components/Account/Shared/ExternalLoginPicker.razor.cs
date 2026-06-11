using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Shared;

/// <summary>
/// Displays external login provider buttons if any are configured, or an informational message if none are available.
/// </summary>
public partial class ExternalLoginPicker(SignInManager<NovaUserEntity> signInManager)
{
    /// <summary>
    /// Stores the list of external authentication schemes available for login.
    /// </summary>
    private AuthenticationScheme[] externalLogins = [];

    /// <summary>
    /// Gets or sets the return URL query parameter that will be passed to the external login handler.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    /// <summary>
    /// Initializes the component by fetching the list of available external authentication schemes.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        externalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToArray();
    }
}
