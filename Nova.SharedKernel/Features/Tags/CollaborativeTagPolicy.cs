namespace Nova.SharedKernel.Features.Tags;

/// <summary>Shared naming and initial color policy for collaborative trait definitions.</summary>
public static class CollaborativeTagPolicy
{
    /// <summary>Accessible Fieldhouse teal used for newly created collaborative traits.</summary>
    public const string DefaultColor = "#146C63";

    /// <summary>Trims and collapses whitespace while retaining the entered casing and punctuation.</summary>
    /// <param name="label">The entered label.</param>
    /// <returns>The display label.</returns>
    public static string NormalizeDisplayName(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Produces the unique comparison key for a display label.</summary>
    /// <param name="label">The entered or stored label.</param>
    /// <returns>The case-insensitive comparison key.</returns>
    public static string NormalizeKey(string label) => NormalizeDisplayName(label).ToUpperInvariant();
}
