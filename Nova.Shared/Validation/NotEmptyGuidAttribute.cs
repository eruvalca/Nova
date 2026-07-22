using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Validation;

/// <summary>
/// Rejects an empty <see cref="Guid"/> while allowing null values to be owned by <see cref="RequiredAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotEmptyGuidAttribute : ValidationAttribute
{
    /// <summary>
    /// Determines whether the supplied value is either not a GUID or a non-empty GUID.
    /// </summary>
    /// <param name="value">The value being validated.</param>
    /// <returns><see langword="false"/> only when <paramref name="value"/> is <see cref="Guid.Empty"/>.</returns>
    public override bool IsValid(object? value) => value is not Guid guid || guid != Guid.Empty;
}
