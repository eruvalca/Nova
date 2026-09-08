#pragma warning disable CA1055, CA1056 // Razor bindings and NavigationManager consume these relative route strings.
using Microsoft.AspNetCore.Components;

namespace Nova.UI.Features.Account.Pages;

/// <summary>
/// The required profile photo page: hosts the photo editor where the user uploads and crops
/// their profile photo. Rendered interactively (Auto) because cropping requires JS interop.
/// </summary>
public partial class ProfilePhoto
{
    /// <summary>
    /// Gets or sets the local URL to return to after the photo is saved.
    /// </summary>
    [SupplyParameterFromQuery]
    public string? ReturnUrl { get; set; }
}


#pragma warning restore CA1055, CA1056
