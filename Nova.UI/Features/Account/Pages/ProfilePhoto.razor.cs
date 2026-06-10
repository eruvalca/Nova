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
