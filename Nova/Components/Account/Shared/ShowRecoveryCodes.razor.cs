using Microsoft.AspNetCore.Components;

namespace Nova.Components.Account.Shared;

/// <summary>
/// Displays recovery codes for account security and recovery purposes.
/// </summary>
public partial class ShowRecoveryCodes
{
    /// <summary>
    /// Gets or sets the array of recovery codes to display.
    /// </summary>
    [Parameter]
    public string[] RecoveryCodes { get; set; } = [];

    /// <summary>
    /// Gets or sets an optional status message to display above the recovery codes.
    /// </summary>
    [Parameter]
    public string? StatusMessage { get; set; }
}
