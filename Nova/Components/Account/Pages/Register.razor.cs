using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Nova.Entities;
using Nova.Shared.Security;

namespace Nova.Components.Account.Pages;

/// <summary>
/// Handles new user registration, including account creation, role assignment, and email confirmation.
/// </summary>
public partial class Register(
    UserManager<NovaUserEntity> userManager,
    IUserStore<NovaUserEntity> userStore,
    SignInManager<NovaUserEntity> signInManager,
    IEmailSender<NovaUserEntity> emailSender,
    NavigationManager navigationManager,
    IdentityRedirectManager redirectManager,
    ILogger<Register> logger)
{
    /// <summary>
    /// Stores validation errors from user creation attempts.
    /// </summary>
    private IEnumerable<IdentityError>? identityErrors;

    /// <summary>
    /// Gets or sets the registration form input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Gets or sets the return URL from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    /// <summary>
    /// Gets the error message to display, formatted from identity validation errors.
    /// </summary>
    private string? Message => identityErrors is null ? null : $"Error: {string.Join(", ", identityErrors.Select(error =>
error.Description))}";

    /// <summary>
    /// Initializes the registration form input model.
    /// </summary>
    protected override void OnInitialized() => Input ??= new();

    /// <summary>
    /// Creates a new user account with the provided credentials and sends a confirmation email.
    /// </summary>
    /// <param name="editContext">The form edit context for validation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RegisterUser(EditContext editContext)
    {
        var user = CreateUser();

        await userStore.SetUserNameAsync(user, Input.Email, ComponentCancellationToken);
        var emailStore = GetEmailStore();
        await emailStore.SetEmailAsync(user, Input.Email, ComponentCancellationToken);
        var result = await userManager.CreateAsync(user, Input.Password);

        if (!result.Succeeded)
        {
            identityErrors = result.Errors;
            return;
        }

        LogUserCreated();

        var roleResult = await userManager.AddToRoleAsync(user, Roles.StandardUser);
        if (!roleResult.Succeeded)
        {
            LogRoleAssignmentFailed(
                Roles.StandardUser,
                string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

        var userId = await userManager.GetUserIdAsync(user);
        var code = await userManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var callbackUrl = navigationManager.GetUriWithQueryParameters(
            navigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
            new Dictionary<string, object?> { ["userId"] = userId, ["code"] = code, ["returnUrl"] = ReturnUrl });

        await emailSender.SendConfirmationLinkAsync(user, Input.Email, HtmlEncoder.Default.Encode(callbackUrl));

        if (userManager.Options.SignIn.RequireConfirmedAccount)
        {
            redirectManager.RedirectTo(
                "Account/RegisterConfirmation",
                new() { ["email"] = Input.Email, ["returnUrl"] = ReturnUrl });
        }
        else
        {
            await signInManager.SignInAsync(user, isPersistent: false);

            // A profile photo is required: send new users straight to the photo step.
            // The ProfilePhotoGateMiddleware enforces this as a backstop.
            redirectManager.RedirectTo(
                "Account/ProfilePhoto",
                new() { ["returnUrl"] = ReturnUrl });
        }
    }

    /// <summary>
    /// Creates a new user entity instance with first and last names from the input model.
    /// </summary>
    /// <returns>A new user entity with trimmed first and last names.</returns>
    private NovaUserEntity CreateUser() => new()
    {
        FirstName = Input.FirstName.Trim(),
        LastName = Input.LastName.Trim()
    };

    /// <summary>
    /// Gets the email store for the user manager, or throws if email is not supported.
    /// </summary>
    /// <returns>The user email store.</returns>
    /// <exception cref="NotSupportedException">Thrown when the user store does not support email operations.</exception>
    private IUserEmailStore<NovaUserEntity> GetEmailStore()
    {
        if (!userManager.SupportsUserEmail)
        {
            throw new NotSupportedException("The default UI requires a user store with email support.");
        }
        return (IUserEmailStore<NovaUserEntity>)userStore;
    }

    /// <summary>
    /// Logs that a user successfully created a new account with a password.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "User created a new account with password.")]
    private partial void LogUserCreated();

    /// <summary>
    /// Logs that role assignment to a user failed.
    /// </summary>
    /// <param name="role">The role that failed to be assigned.</param>
    /// <param name="errors">Comma-separated error descriptions.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to add user to role {Role}: {Errors}")]
    private partial void LogRoleAssignmentFailed(string role, string errors);

    /// <summary>
    /// Form input model for registration, including first/last name, email, and password.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the user's first name.
        /// </summary>
        [Required]
        [StringLength(100)]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = "";

        /// <summary>
        /// Gets or sets the user's last name.
        /// </summary>
        [Required]
        [StringLength(100)]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = "";

        /// <summary>
        /// Gets or sets the user's email address.
        /// </summary>
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = "";

        /// <summary>
        /// Gets or sets the user's password.
        /// </summary>
        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = "";

        /// <summary>
        /// Gets or sets the password confirmation.
        /// </summary>
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
