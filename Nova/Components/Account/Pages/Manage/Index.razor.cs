using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles displaying and updating the user's profile information.
/// </summary>
public partial class Index(
    UserManager<NovaUserEntity> userManager,
    SignInManager<NovaUserEntity> signInManager,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// Stores the current user entity.
    /// </summary>
    private NovaUserEntity? user;

    /// <summary>
    /// Stores the current username for display.
    /// </summary>
    private string? username;

    /// <summary>
    /// Stores the current phone number before changes.
    /// </summary>
    private string? phoneNumber;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the profile form input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Initializes the component and loads the current user's profile information.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        user = await userManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        username = await userManager.GetUserNameAsync(user);
        phoneNumber = await userManager.GetPhoneNumberAsync(user);

        Input.PhoneNumber ??= phoneNumber;
    }

    /// <summary>
    /// Handles the form submission to update the user's profile.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task OnValidSubmitAsync()
    {
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        if (Input.PhoneNumber != phoneNumber)
        {
            var setPhoneResult = await userManager.SetPhoneNumberAsync(user, Input.PhoneNumber);
            if (!setPhoneResult.Succeeded)
            {
                redirectManager.RedirectToCurrentPageWithStatus("Error: Failed to set phone number.", HttpContext);
                return;
            }
        }

        await signInManager.RefreshSignInAsync(user);
        redirectManager.RedirectToCurrentPageWithStatus("Your profile has been updated", HttpContext);
    }

    /// <summary>
    /// Form input model for profile updates, containing the phone number.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the user's phone number.
        /// </summary>
        [Phone]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }
    }
}
