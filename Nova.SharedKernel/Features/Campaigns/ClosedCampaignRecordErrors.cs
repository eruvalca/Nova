namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Structured integrity conflicts distinguish damaged records from a lifecycle transition.</summary>
public static class ClosedCampaignRecordErrors
{
    /// <summary>A Closed campaign cannot supply a complete attributable record.</summary>
    public const string Integrity = "closedRecord";
}
