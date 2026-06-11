using System.Diagnostics;
using Microsoft.AspNetCore.Components;

namespace Nova.Components.Pages;

/// <summary>
/// Displays the generic error page with the current request trace identifier.
/// </summary>
public partial class Error
{
    /// <summary>
    /// Gets or sets the current HTTP context supplied during static SSR rendering.
    /// This is <see langword="null"/> outside of static server-side rendering.
    /// </summary>
    [CascadingParameter]
    private HttpContext? HttpContext { get; set; }

    /// <summary>
    /// Gets or sets the request identifier displayed to the user for diagnostic correlation.
    /// </summary>
    private string? RequestId { get; set; }

    /// <summary>
    /// Gets a value indicating whether a request identifier is available to display.
    /// </summary>
    protected bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    /// <summary>
    /// Captures the current activity or HTTP trace identifier for display.
    /// </summary>
    protected override void OnInitialized() =>
        RequestId = Activity.Current?.Id ?? HttpContext?.TraceIdentifier;
}
