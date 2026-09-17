namespace Nova.SharedKernel.Features.Players;

/// <summary>
/// Club-wide directory counts and graduation-year choices, independent of the displayed page.
/// Summary, choices, and roster reads are eventually consistent rather than one atomic snapshot.
/// </summary>
public sealed record PlayerDirectorySummary
{
    /// <summary>Gets the number of active club players.</summary>
    public required int ActiveCount { get; init; }

    /// <summary>Gets the number of archived club players.</summary>
    public required int ArchivedCount { get; init; }

    /// <summary>Gets distinct ascending years across both views, within the supported 2000–2100 range.</summary>
    public required IReadOnlyList<int> GraduationYears { get; init; }
}
