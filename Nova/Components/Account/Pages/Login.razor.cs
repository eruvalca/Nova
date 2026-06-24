using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Identity;
using Nova.Entities;

namespace Nova.Components.Account.Pages;

/// <summary>
/// Handles user login with support for passwords and passkeys, redirecting to 2FA or lockout as needed.
/// </summary>
public partial class Login(
    SignInManager<NovaUserEntity> signInManager,
    NavigationManager navigationManager,
    IdentityRedirectManager redirectManager,
    ILogger<Login> logger)
{
    /// <summary>
    /// Stores the error message to display when login fails.
    /// </summary>
    private string? errorMessage;

    /// <summary>
    /// Manages form validation state for the login form.
    /// </summary>
    private EditContext editContext = default!;

    /// <summary>
    /// Gets the cascading HTTP context from the parent component.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the login form input model supplied from the form post.
    /// </summary>
    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    /// <summary>
    /// Gets or sets the return URL from the query string.
    /// </summary>
    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    /// <summary>
    /// Gets the Register page URL carrying the current return URL.
    /// </summary>
    protected string RegisterUrl => navigationManager.GetUriWithQueryParameters(
        "Account/Register", new Dictionary<string, object?> { ["ReturnUrl"] = ReturnUrl });

    /// <summary>
    /// Initializes the login form and clears any existing external authentication cookie on GET requests.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        editContext = new EditContext(Input);

        if (HttpMethods.IsGet(HttpContext.Request.Method))
        {
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    /// <summary>
    /// Attempts to log in the user with either a passkey or password, handling 2FA, lockout, and redirects.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoginUser()
    {
        if (!string.IsNullOrEmpty(Input.Passkey?.Error))
        {
            errorMessage = $"Error: {Input.Passkey.Error}";
            return;
        }

        SignInResult result;
        if (!string.IsNullOrEmpty(Input.Passkey?.CredentialJson))
        {
            // When performing passkey sign-in, don't perform form validation.
            result = await signInManager.PasskeySignInAsync(Input.Passkey.CredentialJson);
        }
        else
        {
            // If doing a password sign-in, validate the form.
            if (!editContext.Validate())
            {
                return;
            }

            // This doesn't count login failures towards account lockout
            // To enable password failures to trigger account lockout, set lockoutOnFailure: true
            result = await signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure:
false);
        }

        if (result.Succeeded)
        {
            LogUserLoggedIn();
            redirectManager.RedirectTo(ReturnUrl);
        }
        else if (result.RequiresTwoFactor)
        {
            redirectManager.RedirectTo(
                "Account/LoginWith2fa",
                new() { ["returnUrl"] = ReturnUrl, ["rememberMe"] = Input.RememberMe });
        }
        else if (result.IsLockedOut)
        {
            LogUserLockedOut();
            redirectManager.RedirectTo("Account/Lockout");
        }
        else
        {
            errorMessage = "Error: Invalid login attempt.";
        }
    }

    /// <summary>
    /// Logs that a user successfully logged in.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "User logged in.")]
    private partial void LogUserLoggedIn();

    /// <summary>
    /// Logs that a user account has been locked out.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "User account locked out.")]
    private partial void LogUserLockedOut();

    /// <summary>
    /// Form input model for login, including email, password, remember-me flag, and optional passkey.
    /// </summary>
    private sealed class InputModel
    {
        /// <summary>
        /// Gets or sets the user's email address.
        /// </summary>
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        /// <summary>
        /// Gets or sets the user's password.
        /// </summary>
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        /// <summary>
        /// Gets or sets a value indicating whether the login should be remembered.
        /// </summary>
        [Display(Name = "Remember me?")]
        public bool RememberMe { get; set; }

        /// <summary>
        /// Gets or sets the optional passkey credential for passkey-based login.
        /// </summary>
        public PasskeyInputModel? Passkey { get; set; }
    }
}
