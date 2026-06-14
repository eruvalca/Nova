using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Validation;

/// <summary>
/// Runs <see cref="System.ComponentModel.DataAnnotations"/> validation against an input object and
/// projects the results into the <c>Dictionary&lt;string, string[]&gt;</c> shape consumed by
/// <c>ServiceProblem.Validation</c>. Use this in service methods so validation rules live on the
/// input record (single source of truth) instead of being hand-coded per service.
/// </summary>
public static class InputValidator
{
    /// <summary>
    /// Validates <paramref name="input"/> against all DataAnnotations attributes declared on its
    /// members.
    /// </summary>
    /// <typeparam name="T">The input type being validated.</typeparam>
    /// <param name="input">The instance to validate.</param>
    /// <returns>
    /// A dictionary mapping each invalid member name to its error messages. Empty when the input is
    /// valid. Errors with no associated member name are grouped under the empty-string key.
    /// </returns>
    public static Dictionary<string, string[]> Validate<T>(T input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var context = new ValidationContext(input);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, context, results, validateAllProperties: true);

        return results
            .SelectMany(
                result => result.MemberNames.DefaultIfEmpty(string.Empty),
                (result, member) => (Member: member, Message: result.ErrorMessage ?? string.Empty))
            .GroupBy(entry => entry.Member, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.Message).ToArray(),
                StringComparer.Ordinal);
    }
}
