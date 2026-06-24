using System.Buffers.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Pages.Manage;

/// <summary>
/// Handles management of passkeys for the user account, including adding, renaming, and deleting passkeys.
/// </summary>
public partial class Passkeys(
    UserManager<NovaUserEntity> userManager,
    SignInManager<NovaUserEntity> signInManager,
    IdentityRedirectManager redirectManager)
{
    /// <summary>
    /// The maximum number of passkeys allowed per user.
    /// </summary>
    private const int MaxPasskeyCount = 100;

    /// <summary>
    /// Stores the current user entity.
    /// </summary>
    private NovaUserEntity? user;

    /// <summary>
    /// Stores the list of passkeys currently registered for the user.
    /// </summary>
    private IList<UserPasskeyInfo>? currentPasskeys;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the action to perform on a passkey (rename or delete) supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private string? Action { get; set; }

    /// <summary>
    /// Gets or sets the credential ID of the passkey to operate on, supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private string? CredentialId { get; set; }

    /// <summary>
    /// Gets or sets the passkey input model supplied from the add-passkey form post.
    /// </summary>
    [SupplyParameterFromForm(FormName = "add-passkey")]
    private PasskeyInputModel Input { get; set; } = default!;

    /// <summary>
    /// Initializes the component, loading the current user and their registered passkeys.
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
        currentPasskeys = await userManager.GetPasskeysAsync(user);
    }

    /// <summary>
    /// Handles the form submission to add a new passkey to the user account.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task AddPasskey()
    {
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        if (!string.IsNullOrEmpty(Input.Error))
        {
            redirectManager.RedirectToCurrentPageWithStatus($"Error: {Input.Error}", HttpContext);
            return;
        }

        if (string.IsNullOrEmpty(Input.CredentialJson))
        {
            redirectManager.RedirectToCurrentPageWithStatus("Error: The browser did not provide a passkey.", HttpContext);
            return;
        }

        if (currentPasskeys!.Count >= MaxPasskeyCount)
        {
            redirectManager.RedirectToCurrentPageWithStatus($"Error: You have reached the maximum number of allowed passkeys.",
                HttpContext);
            return;
        }

        var attestationResult = await signInManager.PerformPasskeyAttestationAsync(Input.CredentialJson);
        if (!attestationResult.Succeeded)
        {
            redirectManager.RedirectToCurrentPageWithStatus($"Error: Could not add the passkey: {attestationResult.Failure.Message}", HttpContext);
            return;
        }

        var addPasskeyResult = await userManager.AddOrUpdatePasskeyAsync(user, attestationResult.Passkey);
        if (!addPasskeyResult.Succeeded)
        {
            redirectManager.RedirectToCurrentPageWithStatus("Error: The passkey could not be added to your account.", HttpContext);
            return;
        }

        // Immediately prompt the user to enter a name for the credential
        var credentialIdBase64Url = Base64Url.EncodeToString(attestationResult.Passkey.CredentialId);
        redirectManager.RedirectTo($"Account/Manage/RenamePasskey/{credentialIdBase64Url}");
    }

    /// <summary>
    /// Handles the form submission to update a passkey (either rename or delete based on the action).
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task UpdatePasskey()
    {
        switch (Action)
        {
            case "rename":
                redirectManager.RedirectTo($"Account/Manage/RenamePasskey/{CredentialId}");
                break;
            case "delete":
                await DeletePasskey();
                break;
            default:
                redirectManager.RedirectToCurrentPageWithStatus($"Error: Unknown action '{Action}'.", HttpContext);
                break;
        }
    }

    /// <summary>
    /// Handles the deletion of a passkey from the user account.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task DeletePasskey()
    {
        if (user is null)
        {
            redirectManager.RedirectToInvalidUser(userManager, HttpContext);
            return;
        }

        byte[] credentialId;
        try
        {
            credentialId = Base64Url.DecodeFromChars(CredentialId);
        }
        catch (FormatException)
        {
            redirectManager.RedirectToCurrentPageWithStatus("Error: The specified passkey ID had an invalid format.", HttpContext);
            return;
        }

        var result = await userManager.RemovePasskeyAsync(user, credentialId);
        if (!result.Succeeded)
        {
            redirectManager.RedirectToCurrentPageWithStatus("Error: The passkey could not be deleted.", HttpContext);
            return;
        }

        redirectManager.RedirectToCurrentPageWithStatus("Passkey deleted successfully.", HttpContext);
    }
}
