using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Nova.Entities;

namespace Nova.Components.Account;

/// <summary>
/// A no-operation email sender implementation for Identity operations.
/// Logs email requests without actually sending them to external services.
/// </summary>
/// <remarks>
/// Remove the "else if (EmailSender is IdentityNoOpEmailSender)" block from RegisterConfirmation.razor after updating with a real implementation.
/// </remarks>
internal sealed class IdentityNoOpEmailSender : IEmailSender<NovaUserEntity>
{
    /// <summary>
    /// Gets the underlying no-operation email sender instance.
    /// </summary>
    private readonly IEmailSender emailSender = new NoOpEmailSender();

    /// <summary>
    /// Sends a confirmation link to the user's email address.
    /// </summary>
    /// <param name="user">The user to send the confirmation to.</param>
    /// <param name="email">The email address to send to.</param>
    /// <param name="confirmationLink">The confirmation URL.</param>
    /// <returns>A completed task.</returns>
    public Task SendConfirmationLinkAsync(NovaUserEntity user, string email, string confirmationLink) =>
        emailSender.SendEmailAsync(email, "Confirm your email", $"Please confirm your account by <a href='{confirmationLink}'>clicking here</a>.");

    /// <summary>
    /// Sends a password reset link to the user's email address.
    /// </summary>
    /// <param name="user">The user to send the reset link to.</param>
    /// <param name="email">The email address to send to.</param>
    /// <param name="resetLink">The password reset URL.</param>
    /// <returns>A completed task.</returns>
    public Task SendPasswordResetLinkAsync(NovaUserEntity user, string email, string resetLink) =>
        emailSender.SendEmailAsync(email, "Reset your password", $"Please reset your password by <a href='{resetLink}'>clicking here</a>.");

    /// <summary>
    /// Sends a password reset code to the user's email address.
    /// </summary>
    /// <param name="user">The user to send the reset code to.</param>
    /// <param name="email">The email address to send to.</param>
    /// <param name="resetCode">The password reset code.</param>
    /// <returns>A completed task.</returns>
    public Task SendPasswordResetCodeAsync(NovaUserEntity user, string email, string resetCode) =>
        emailSender.SendEmailAsync(email, "Reset your password", $"Please reset your password using the following code: {resetCode}");
}
