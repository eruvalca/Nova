#pragma warning disable CA1515 // Razor generates a public component partial class.
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Common;

/// <summary>
/// Renders a navigation menu for the account management section with links to various manage pages.
/// Conditionally displays external login links based on whether external authentication schemes are configured.
/// </summary>
public partial class ManageNavMenu(SignInManager<NovaUserEntity> signInManager)
{
    /// <summary>
    /// Indicates whether any external authentication schemes are configured.
    /// </summary>
    private bool _hasExternalLogins;

    /// <summary>
    /// Initializes the component by checking if external authentication schemes are available.
    /// </summary>
    protected override async Task OnInitializedAsync() => _hasExternalLogins = (await signInManager.GetExternalAuthenticationSchemesAsync()).Any();
}
