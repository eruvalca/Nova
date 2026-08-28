using Microsoft.AspNetCore.Components;

namespace Nova.Components.Account.Shared;

/// <summary>
/// Renders a form label linked to a control by id, with an optional "(optional)" hint.
/// </summary>
public partial class AccountFormLabel
{
    /// <summary>
    /// Gets or sets the id of the control this label is associated with.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public string For { get; set; } = default!;

    /// <summary>
    /// Gets or sets the label text.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public string Text { get; set; } = default!;

    /// <summary>
    /// Gets or sets a value indicating whether the field is optional, shown as a hint.
    /// </summary>
    [Parameter]
    public bool Optional { get; set; }
}
