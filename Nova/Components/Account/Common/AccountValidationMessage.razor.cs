#pragma warning disable CA1515 // Razor generates a public component partial class.
using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;

namespace Nova.Components.Account.Common;

/// <summary>
/// Renders the validation message for a form field using the EditContext validation pipeline.
/// </summary>
/// <typeparam name="TValue">The type of the validated field.</typeparam>
public partial class AccountValidationMessage<TValue>
{
    /// <summary>
    /// Gets or sets the expression identifying the field to validate.
    /// </summary>
    [Parameter]
    [EditorRequired]
    public Expression<Func<TValue>> For { get; set; } = default!;
}
