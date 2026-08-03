using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Validation;

/// <summary>
/// Rejects the default <see cref="DateOnly"/> value while allowing null values to be owned by
/// <see cref="RequiredAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotDefaultDateOnlyAttribute : ValidationAttribute
{
    /// <summary>
    /// Determines whether the supplied value is either not a date or a non-default date.
    /// </summary>
    /// <param name="value">The value being validated.</param>
    /// <returns><see langword="false"/> only when <paramref name="value"/> is the default date.</returns>
    public override bool IsValid(object? value) =>
        value is not DateOnly date || date != default;
}
