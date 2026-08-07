using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.UI.Components;
using Nova.UI.Features.Campaigns.Components;

namespace Nova.UI.Features.Campaigns.Pages;

/// <summary>
/// Renders the administrator campaign-creation workflow with live enrollment preview counts.
/// </summary>
/// <param name="campaignQueryService">The campaign creation-setup query service.</param>
/// <param name="campaignCreationService">The campaign creation service.</param>
/// <param name="navigationManager">The navigation manager used for redirects.</param>
public partial class NewCampaign(
    ICampaignQueryService campaignQueryService,
    ICampaignCreationService campaignCreationService,
    NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// The loaded creation setup data, or <see langword="null"/> when unavailable.
    /// </summary>
    private CampaignCreationSetupResult? _setup;

    /// <summary>
    /// The current page-level error message for setup loading.
    /// </summary>
    private string? _pageError;

    /// <summary>
    /// The current form-level error message for creation failures.
    /// </summary>
    private string? _formError;

    /// <summary>
    /// Indicates whether setup data is being loaded.
    /// </summary>
    private bool _isLoading;

    /// <summary>
    /// Indicates whether a creation submission is in progress.
    /// </summary>
    private bool _isSubmitting;

    /// <summary>
    /// The create-campaign input model bound to the form.
    /// </summary>
    private readonly CampaignCreateFormState _createForm = CampaignCreateFormState.CreateDefault();

    /// <summary>
    /// Gets or sets the persisted startup setup snapshot used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public CampaignCreationSetupResult? PersistedSetup { get; set; }

    /// <summary>
    /// Gets or sets the persisted startup page error used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? PersistedPageError { get; set; }

    /// <summary>
    /// Gets or sets whether startup initialization already completed during prerender.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        if (Initialized)
        {
            _setup = PersistedSetup;
            _pageError = PersistedPageError;
            _isLoading = false;
            EnsureOperationId();
            return;
        }

        _isLoading = true;
        EnsureOperationId();
        await LoadSetupAsync();
        PersistStartupState();
        Initialized = true;
    }

    /// <summary>
    /// Assigns a fresh idempotency identifier when the form has not started one yet.
    /// </summary>
    private void EnsureOperationId()
    {
        if (_createForm.OperationId == Guid.Empty)
        {
            _createForm.OperationId = Guid.CreateVersion7();
        }
    }

    /// <summary>
    /// Reloads the creation setup data.
    /// </summary>
    /// <returns>A task that completes when loading and state updates are finished.</returns>
    private async Task LoadSetupAsync()
    {
        _isLoading = true;
        _pageError = null;

        ServiceResult<CampaignCreationSetupResult> result;
        try
        {
            result = await campaignQueryService.GetCreationSetupAsync(ComponentCancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (ComponentCancellationToken.IsCancellationRequested)
            {
                return;
            }

            _pageError = "Could not reach the server. Check your connection and retry.";
            _setup = null;
            PersistStartupState();
            _isLoading = false;
            return;
        }

        result.Switch(
            setup => _setup = setup,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                _pageError = FirstNonBlank(problem.Detail, "Failed to load campaign creation setup. Please retry.");
                _setup = null;
            });

        PersistStartupState();
        _isLoading = false;
    }

    /// <summary>
    /// Persists the current startup setup/error state for prerender-to-interactive restoration.
    /// </summary>
    private void PersistStartupState()
    {
        PersistedSetup = _setup;
        PersistedPageError = _pageError;
    }

    /// <summary>
    /// Reloads setup data after a page-level error.
    /// </summary>
    /// <returns>A task that completes when the reload finishes.</returns>
    private Task ReloadAsync() => LoadSetupAsync();

    /// <summary>
    /// Creates the campaign from the validated form state and navigates to the campaign list.
    /// </summary>
    /// <param name="model">The validated form state.</param>
    /// <returns>A task that completes when the creation request finishes.</returns>
    private async Task CreateCampaignAsync(CampaignCreateFormState model)
    {
        if (_isSubmitting)
        {
            return;
        }

        _isSubmitting = true;
        _formError = null;

        try
        {
            var result = await campaignCreationService.CreateAsync(model.ToCreateInput(), ComponentCancellationToken);
            result.Switch(
                _ => navigationManager.NavigateTo("campaigns"),
                problem =>
                {
                    if (problem.Kind == ServiceProblemKind.Forbidden)
                    {
                        navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                        return;
                    }

                    _formError = problem.Kind == ServiceProblemKind.Conflict
                        ? FirstNonBlank(problem.Detail, "A campaign with these details already exists. Review the campaign list before retrying.")
                        : FirstNonBlank(problem.Detail, FlattenValidationErrors(problem), "Failed to create the campaign. Please retry.");
                });
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (ComponentCancellationToken.IsCancellationRequested)
            {
                return;
            }

            _formError = "Could not reach the server. Check your connection and retry; the same request will resume safely.";
        }
        finally
        {
            _isSubmitting = false;
        }
    }

    /// <summary>
    /// Returns the first non-blank message from the supplied candidates.
    /// </summary>
    /// <param name="candidates">The candidate messages in preference order.</param>
    /// <returns>The first non-blank candidate.</returns>
    private static string FirstNonBlank(params string?[] candidates)
        => candidates.First(candidate => !string.IsNullOrWhiteSpace(candidate))!;

    /// <summary>
    /// Flattens field-level validation messages when the problem carries no detail text.
    /// </summary>
    /// <param name="problem">The service problem.</param>
    /// <returns>The joined field messages, or <see langword="null"/> when the problem has no errors.</returns>
    private static string? FlattenValidationErrors(ServiceProblem problem)
        => problem.Errors is { Count: > 0 }
            ? string.Join(" ", problem.Errors.SelectMany(pair => pair.Value))
            : null;

    /// <summary>
    /// Cancels creation and returns to the campaign list.
    /// </summary>
    private void Cancel() => navigationManager.NavigateTo("campaigns");
}
