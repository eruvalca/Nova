namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Shared condition keys for the campaign close blockers reported by the foundation closure policy.
/// These literals are the single source of truth shared by <c>CampaignClosurePolicy</c> and the
/// closeout-readiness DTO mapping so a condition key can never drift between the server policy and
/// the read contract.
/// </summary>
public static class CloseoutBlockerConditions
{
    /// <summary>
    /// The condition key for participation records that still lack a final placement outcome.
    /// </summary>
    public const string Outcomes = "outcomes";

    /// <summary>
    /// The condition key for assigned participation records that fail team eligibility.
    /// </summary>
    public const string Eligibility = "eligibility";

    /// <summary>
    /// The condition key for assigned participation records that reference archived teams.
    /// </summary>
    public const string ArchivedTeams = "archivedTeams";
}
