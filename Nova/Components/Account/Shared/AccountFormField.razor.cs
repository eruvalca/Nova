using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;

namespace Nova.Components.Account.Shared;

/// <summary>
/// Groups a labeled account form field: label, control slot, validation message, and optional help text.
/// </summary>
/// <typeparam name="TValue">The type of the validated field.</typeparam>
public partial class AccountFormField<TValue>
{
    /// <summary>
    /// Gets or sets the expression identifying the field to validate.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public Expression<Func<TValue>> For { get; set; } = default!;

    /// <summary>
    /// Gets or sets the id used to associate the label with the control inside <see cref="ChildContent"/>.
    /// </summary>
    [Parameter]
    public string? FieldId { get; set; }

    /// <summary>
    /// Gets or sets the label text rendered above the control.
    /// </summary>
    [Parameter]
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field is optional, shown as a hint.
    /// </summary>
    [Parameter]
    public bool Optional { get; set; }

    /// <summary>
    /// Gets or sets optional help text rendered below the control.
    /// </summary>
    [Parameter]
    public string? HelpText { get; set; }

    /// <summary>
    /// Gets or sets the control content rendered between the label and validation message.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public RenderFragment? ChildContent { get; set; }
}
