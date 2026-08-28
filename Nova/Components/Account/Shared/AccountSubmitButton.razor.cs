using Microsoft.AspNetCore.Components;

namespace Nova.Components.Account.Shared;

/// <summary>
/// Renders a full-width or inline submit button for account forms with a minimum touch size.
/// </summary>
public partial class AccountSubmitButton
{
    /// <summary>
    /// Gets or sets the button label.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public string Text { get; set; } = default!;

    /// <summary>
    /// Gets or sets a value indicating whether the button spans the form width.
    /// </summary>
    [Parameter]
    public bool Block { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the button is disabled.
    /// </summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>
    /// Gets the Bootstrap variant class for the button (primary or secondary).
    /// </summary>
    private string ButtonVariant => Variant == AccountButtonKind.Secondary ? "btn-secondary" : "btn-primary";

    /// <summary>
    /// Gets or sets the visual variant of the button.
    /// </summary>
    [Parameter]
    public AccountButtonKind Variant { get; set; } = AccountButtonKind.Primary;

    /// <summary>
    /// Defines the visual variants available to the account submit button.
    /// </summary>
    public enum AccountButtonKind
    {
        /// <summary>The lead action: Wayfinding Teal.</summary>
        Primary,

        /// <summary>A quiet follow-up action.</summary>
        Secondary,
    }
}
