namespace Nova.SharedKernel.Features.Common;

/// <summary>
/// Shared spreadsheet-formula safety for CSV cells. Imports reject formula-like values so untrusted
/// cells cannot become executable content; exports neutralize the same values so exported text is
/// never interpreted as a formula. Both paths use <see cref="IsFormulaLike"/> so the rule cannot drift.
/// </summary>
public static class CsvCellSafety
{
    /// <summary>The text marker a spreadsheet interprets as "treat this cell as literal text".</summary>
    public const char TextMarker = '\'';

    /// <summary>
    /// Determines whether a spreadsheet would interpret the value as a formula. Leading control
    /// characters and a formula character that follows only spaces count as formula-like, because a
    /// spreadsheet still evaluates them after trimming.
    /// </summary>
    /// <param name="value">The untrusted cell value.</param>
    /// <returns><see langword="true"/> when the value must be rejected on import or escaped on export.</returns>
    public static bool IsFormulaLike(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value[0] is '\t' or '\r' or '\n')
        {
            return true;
        }

        var firstMeaningfulCharacter = value.FirstOrDefault(character => character != ' ');
        return firstMeaningfulCharacter is '\t' or '\r' or '\n' or '=' or '+' or '-' or '@';
    }

    /// <summary>
    /// Neutralizes a formula-like value by prefixing <see cref="TextMarker"/>, which keeps the real
    /// text visible while a spreadsheet treats the cell as literal text. Values that are not
    /// formula-like, including text already carrying the marker, are returned unchanged.
    /// </summary>
    /// <param name="value">The cell value to make safe for a spreadsheet.</param>
    /// <returns>The value unchanged, or the value prefixed with the text marker.</returns>
    public static string EscapeFormula(string value)
        => IsFormulaLike(value) ? TextMarker + value : value;
}
