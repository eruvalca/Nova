#pragma warning disable CA1515 // Razor generates a public component partial class.
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Common;

/// <summary>
/// Displays external login provider buttons if any are configured, or an informational message if none are available.
/// </summary>
public partial class ExternalLoginPicker(SignInManager<NovaUserEntity> signInManager)
{
    /// <summary>
    /// Stores the list of external authentication schemes available for login.
    /// </summary>
    private AuthenticationScheme[] _externalLogins = [];

    /// <summary>
    /// Gets or sets the return URL query parameter that will be passed to the external login handler.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    /// <summary>
    /// Initializes the component by fetching the list of available external authentication schemes.
    /// </summary>
    protected override async Task OnInitializedAsync() => _externalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).ToArray();
}
