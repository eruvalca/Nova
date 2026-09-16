using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>Displays shared readiness and independent roster evidence for the Close destination.</summary>
public partial class CampaignCloseoutPanel
{
    /// <summary>The authorized campaign detail.</summary>
    [Parameter, EditorRequired] public CampaignDetailResult Detail { get; set; } = null!;
    /// <summary>The workspace-owned lifecycle evidence.</summary>
    [Parameter] public CampaignLifecycleEvidence? Evidence { get; set; }
    /// <summary>The current user, club, campaign and authority owner.</summary>
    [Parameter, EditorRequired] public string Owner { get; set; } = string.Empty;
    /// <summary>The stable refresh generation, independent of readiness completion or failure.</summary>
    [Parameter] public long RefreshGeneration { get; set; }
    /// <summary>Whether readiness is loading.</summary>
    [Parameter] public bool Loading { get; set; }
    /// <summary>The readiness failure, independent of roster loading.</summary>
    [Parameter] public string? Error { get; set; }
    /// <summary>Refreshes authorized detail and readiness before any commitment.</summary>
    [Parameter, EditorRequired] public Func<Task<CampaignLifecycleEvidence?>> RefreshEvidence { get; set; } = null!;
    /// <summary>The independently owned Close discovery state.</summary>
    [Parameter, EditorRequired] public CampaignWorkspaceCloseState State { get; set; } = new();
    /// <summary>Builds native roster discovery links.</summary>
    [Parameter, EditorRequired] public Func<CampaignWorkspaceCloseState, string> BuildCloseUrl { get; set; } = null!;
    /// <summary>Builds a participant's existing Place editor correction link.</summary>
    [Parameter, EditorRequired] public Func<long, string> BuildParticipantUrl { get; set; } = null!;
    /// <summary>Applies search and blocker discovery changes to history.</summary>
    [Parameter] public EventCallback<CampaignWorkspaceCloseState> OnStateChanged { get; set; }
    /// <summary>Refreshes the campaign when roster evidence observes a lifecycle transition.</summary>
    [Parameter] public EventCallback OnLifecycleChanged { get; set; }

    private Task<CampaignLifecycleEvidence?> RetryAsync() => RefreshEvidence();
    private CampaignCloseoutReadinessDto? Readiness => Evidence?.Readiness;
    private static string BlockerLabel(CampaignCloseoutBlockerDto blocker) => blocker.Condition switch
    {
        CloseoutBlockerConditions.Outcomes => $"{blocker.Count} missing campaign outcomes",
        CloseoutBlockerConditions.Eligibility => $"{blocker.Count} incompatible assignments",
        CloseoutBlockerConditions.ArchivedTeams => $"{blocker.Count} assignments use an archived team",
        _ => blocker.Message,
    };
}
