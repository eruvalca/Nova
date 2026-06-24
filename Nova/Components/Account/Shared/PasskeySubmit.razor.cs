using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;

namespace Nova.Components.Account.Shared;

/// <summary>
/// Provides a secure button for submitting passkey operations with CSRF protection tokens.
/// </summary>
public partial class PasskeySubmit(IAntiforgery antiforgery)
{
    /// <summary>
    /// Stores the antiforgery token set for the current HTTP context.
    /// </summary>
    private AntiforgeryTokenSet? tokens;

    /// <summary>
    /// Gets or sets the cascading HTTP context used to generate antiforgery tokens.
    /// </summary>
    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    /// <summary>
    /// Gets or sets the type of passkey operation to perform (Create or Request).
    /// </summary>
    [Parameter]
    [EditorRequired]
    public PasskeyOperation Operation { get; set; }

    /// <summary>
    /// Gets or sets the name of the passkey operation handler endpoint.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public string Name { get; set; } = default!;

    /// <summary>
    /// Gets or sets an optional email field name for the passkey operation.
    /// </summary>
    [Parameter]
    public string? EmailName { get; set; }

    /// <summary>
    /// Gets or sets the child content to render inside the submit button.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Gets or sets additional HTML attributes to apply to the submit button.
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>
    /// Initializes the component by generating antiforgery tokens for the current HTTP context.
    /// </summary>
    protected override void OnInitialized() => tokens = antiforgery.GetTokens(HttpContext);
}
