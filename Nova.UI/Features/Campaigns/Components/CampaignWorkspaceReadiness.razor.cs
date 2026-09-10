using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>Owns the independent authoritative Close-readiness region.</summary>
/// <param name="queries">The closeout query service.</param>
public partial class CampaignWorkspaceReadiness(ICampaignCloseoutQueryService queries)
{
    /// <summary>The campaign being oriented.</summary>
    [Parameter] public long CampaignId { get; set; }
    /// <summary>The authorized lifecycle, used to disable stale Active context immediately.</summary>
    [Parameter] public CampaignStatus Status { get; set; }
    /// <summary>The contextual Close destination.</summary>
    [Parameter] public string CloseDestination { get; set; } = string.Empty;
    /// <summary>The authority and refresh revision owning this region.</summary>
    [Parameter] public string? Owner { get; set; }
    /// <summary>Requests authoritative lifecycle reconciliation after conflicting evidence.</summary>
    [Parameter] public EventCallback OnLifecycleChanged { get; set; }

    private string? _loadedKey;
    private static string BlockerLabel(CampaignCloseoutBlockerDto blocker) => blocker.Condition switch
    {
        CloseoutBlockerConditions.Outcomes => $"{blocker.Count} missing campaign outcome{(blocker.Count == 1 ? string.Empty : "s")}",
        CloseoutBlockerConditions.Eligibility => $"{blocker.Count} assignment{(blocker.Count == 1 ? " needs" : "s need")} correction",
        CloseoutBlockerConditions.ArchivedTeams => $"{blocker.Count} assignment{(blocker.Count == 1 ? " uses" : "s use")} an archived team",
        _ => blocker.Message,
    };
    private int _request;
    private bool _loading;
    private bool _error;
    private CampaignCloseoutReadinessDto? _readiness;

    /// <summary>Whether this regional startup read finished before interactive attachment.</summary>
    [PersistentState] public bool Initialized { get; set; }
    /// <summary>The authority, campaign and lifecycle owning the persisted region.</summary>
    [PersistentState] public string? PersistedOwner { get; set; }
    /// <summary>The authoritative regional result.</summary>
    [PersistentState] public CampaignCloseoutReadinessDto? PersistedReadiness { get; set; }
    /// <summary>The regional failure, retained until an explicit retry.</summary>
    [PersistentState] public bool PersistedError { get; set; }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var key = $"{Owner}:{CampaignId}:{Status}";
        if (_loadedKey is null && Initialized && string.Equals(PersistedOwner, key, StringComparison.Ordinal))
        {
            _loadedKey = key;
            _readiness = PersistedReadiness;
            _error = PersistedError;
            return;
        }
        if (!string.Equals(key, _loadedKey, StringComparison.Ordinal))
        {
            _loadedKey = key;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        var request = ++_request;
        _readiness = null;
        _error = false;
        _loading = Status == CampaignStatus.Active;
        if (!_loading)
        {
            PersistRegion();
            return;
        }
        try
        {
            var result = await queries.GetCloseoutReadinessAsync(new() { CampaignId = CampaignId }, ComponentCancellationToken);
            if (request != _request || ComponentCancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (result.IsSuccess && result.Value.Status == Status)
            {
                _readiness = result.Value;
            }
            else
            {
                _error = true;
                if (result.IsSuccess)
                {
                    await OnLifecycleChanged.InvokeAsync();
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !ComponentCancellationToken.IsCancellationRequested)
        {
            if (request == _request)
            {
                _error = true;
            }
        }
        finally
        {
            if (request == _request && !ComponentCancellationToken.IsCancellationRequested)
            {
                _loading = false;
                PersistRegion();
            }
        }
    }

    private void PersistRegion()
    {
        Initialized = true;
        PersistedOwner = _loadedKey;
        PersistedReadiness = _readiness;
        PersistedError = _error;
    }
}
