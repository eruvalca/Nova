using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Validation;

/// <summary>
/// Validation attribute that rejects empty and whitespace-only string values; <see langword="null"/>
/// is treated as valid so that <see cref="RequiredAttribute"/> owns the "missing value" message.
/// Apply to string members that must contain at least one non-whitespace character; always pair
/// with <see cref="RequiredAttribute"/> for full coverage.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NotWhitespaceAttribute : ValidationAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NotWhitespaceAttribute"/> class.
    /// </summary>
    public NotWhitespaceAttribute()
        : base("The {0} field must not be empty or whitespace.")
    {
    }

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        // null is treated as valid here so [Required] owns the "missing" message and this
        // attribute owns only the "present but blank" case; combine both for full coverage.
        if (value is null)
        {
            return true;
        }

        if (value is string text)
        {
            return !string.IsNullOrWhiteSpace(text);
        }

        // Non-string values are outside this attribute's concern; treat as valid.
        return true;
    }
}
