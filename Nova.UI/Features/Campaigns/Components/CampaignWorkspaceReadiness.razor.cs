using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>Displays the workspace-owned authoritative Close snapshot.</summary>
public partial class CampaignWorkspaceReadiness
{
    /// <summary>The current authorized lifecycle.</summary>
    [Parameter] public CampaignStatus Status { get; set; }
    /// <summary>The contextual Close destination.</summary>
    [Parameter] public string CloseDestination { get; set; } = string.Empty;
    /// <summary>The workspace snapshot, shared with Close.</summary>
    [Parameter] public CampaignCloseoutReadinessDto? Readiness { get; set; }
    /// <summary>Whether the shared snapshot is loading.</summary>
    [Parameter] public bool Loading { get; set; }
    /// <summary>Whether the shared read failed.</summary>
    [Parameter] public bool Error { get; set; }
    /// <summary>Retries the workspace-owned read.</summary>
    [Parameter] public EventCallback OnRetry { get; set; }
    private static string BlockerLabel(CampaignCloseoutBlockerDto blocker) => blocker.Condition switch
    {
        CloseoutBlockerConditions.Outcomes => $"{blocker.Count} missing campaign outcome{(blocker.Count == 1 ? string.Empty : "s")}",
        CloseoutBlockerConditions.Eligibility => $"{blocker.Count} assignment{(blocker.Count == 1 ? " needs" : "s need")} correction",
        CloseoutBlockerConditions.ArchivedTeams => $"{blocker.Count} assignment{(blocker.Count == 1 ? " uses" : "s use")} an archived team",
        _ => blocker.Message,
    };
}
