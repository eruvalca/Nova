namespace Nova.UI.Common;

/// <summary>Normalizes local correction handoffs without accepting external destinations.</summary>
public static class CorrectionReturnContext
{
    /// <summary>Returns a safe local destination, or null for an absent or invalid handoff.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) { return null; }
        var candidate = value.Trim();
        if (!candidate.StartsWith('/') || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.Contains('\\', StringComparison.Ordinal) || candidate.Any(char.IsControl)
            || !Uri.IsWellFormedUriString(candidate, UriKind.Relative)) { return null; }
        return candidate;
    }
}

