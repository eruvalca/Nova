using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Data;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Displays personal data management options including download and delete functionality.
/// </summary>
public partial class PersonalData(
    UserManager<NovaUserEntity> userManager,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Initializes the component and validates the current user.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        var user = await userManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
        }
    }
}
