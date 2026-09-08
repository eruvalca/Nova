#pragma warning disable CA1515 // Razor generates a public component partial class.
using Microsoft.AspNetCore.Components;

namespace Nova.Components.Account.Common;

/// <summary>
/// Displays recovery codes for account security and recovery purposes.
/// </summary>
public partial class ShowRecoveryCodes
{
    /// <summary>
    /// Gets or sets the recovery codes to display.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string> RecoveryCodes { get; set; } = [];

    /// <summary>
    /// Gets or sets an optional status message to display above the recovery codes.
    /// </summary>
    [Parameter]
    public string? StatusMessage { get; set; }
}
