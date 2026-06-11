using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Data;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles renaming a passkey for the current user.
/// </summary>
public partial class RenamePasskey(
    UserManager<NovaUserEntity> userManager,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// Stores the current user entity.
    /// </summary>
    private NovaUserEntity? user;

    /// <summary>
    /// Stores the passkey information being renamed.
    /// </summary>
    private UserPasskeyInfo? passkey;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the passkey ID from the route parameter.
    /// </summary>
    [Parameter]
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the passkey rename form input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Initializes the component and loads the passkey to be renamed.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        user = (await userManager.GetUserAsync(HttpContext.User))!;
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        byte[] credentialId;
        try
        {
            credentialId = Base64Url.DecodeFromChars(Id);
        }
        catch (FormatException)
        {
            redirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Error: The specified passkey ID had an invalid format.", HttpContext);
            return;
        }

        passkey = await userManager.GetPasskeyAsync(user, credentialId);
        if (passkey is null)
        {
            redirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Error: The specified passkey could not be found.",
HttpContext);
            return;
        }
    }

    /// <summary>
    /// Handles the form submission to rename the passkey.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task Rename()
    {
        passkey!.Name = Input.Name;
        var result = await userManager.AddOrUpdatePasskeyAsync(user!, passkey);
        if (!result.Succeeded)
        {
            redirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Error: The passkey could not be updated.",
HttpContext);
            return;
        }

        redirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Passkey updated successfully.", HttpContext);
    }

    /// <summary>
    /// Form input model for passkey rename operation.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the new name for the passkey.
        /// </summary>
        [Required]
        [StringLength(200, ErrorMessage = "Passkey names must be no longer than {1} characters.")]
        public string Name { get; set; } = "";
    }
}
