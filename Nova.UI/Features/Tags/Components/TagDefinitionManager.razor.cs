using Microsoft.AspNetCore.Components;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.UI.Components;
using OneOf.Types;

namespace Nova.UI.Features.Tags.Components;

/// <summary>
/// Interactive island that lets club administrators manage tag definitions: create, edit, archive, and
/// restore, with an active/archived/all view toggle and a bounded case-insensitive name search.
/// </summary>
/// <param name="queryService">The tag-definition query service.</param>
/// <param name="managementService">The tag-definition create/update service.</param>
/// <param name="lifecycleService">The tag-definition archive/restore service.</param>
/// <param name="navigationManager">The navigation manager used for access-denied redirects.</param>
public partial class TagDefinitionManager(
    ITagDefinitionQueryService queryService,
    ITagDefinitionService managementService,
    ITagDefinitionLifecycleService lifecycleService,
    NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// Gets or sets the loaded tag definitions, persisted across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<TagDefinitionDto>? Tags { get; set; }

    /// <summary>
    /// Gets or sets the initial-load error message, persisted across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets whether startup initialization already completed during prerender.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <summary>
    /// Gets or sets the active lifecycle view (<c>active</c>, <c>archived</c>, or <c>all</c>).
    /// Normalized to <c>active</c> on first initialization.
    /// </summary>
    [PersistentState]
    public string? LifecycleView { get; set; }

    /// <summary>
    /// Gets or sets the currently applied name search term.
    /// </summary>
    [PersistentState]
    public string? AppliedSearch { get; set; }

    private bool _isLoading;
    private bool _isMutating;
    private bool _showForm;
    private TagFormState? _form;
    private string? _formError;
    private string? _actionError;
    private string? _statusMessage;
    private string _searchInput = string.Empty;
    private TagDefinitionDto? _archiveTarget;
    private TagDefinitionDto? _restoreTarget;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        LifecycleView ??= "active";
        AppliedSearch ??= string.Empty;
        _searchInput = AppliedSearch;

        if (Initialized)
        {
            return;
        }

        await LoadTagsAsync();
        Initialized = true;
    }

    /// <summary>
    /// Loads tag definitions matching the current view and search filters.
    /// </summary>
    /// <returns>A task that completes when the list has been refreshed.</returns>
    private async Task LoadTagsAsync()
    {
        _isLoading = true;
        Error = null;
        Tags = null;

        ServiceResult<IReadOnlyList<TagDefinitionDto>> result;
        try
        {
            result = await queryService.GetManagementListAsync(
                new GetTagDefinitionsInput { Search = AppliedSearch, LifecycleStatus = LifecycleView },
                ComponentCancellationToken);
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _ = ex;
            Error = "Failed to load tag definitions. Please retry.";
            _isLoading = false;
            return;
        }

        result.Switch(
            tags => Tags = tags,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    return;
                }

                Error = problem.Detail ?? "Failed to load tag definitions.";
            });

        _isLoading = false;
    }

    /// <summary>
    /// Switches the active lifecycle view and reloads the list.
    /// </summary>
    /// <param name="view">The lifecycle view to apply.</param>
    /// <returns>A task that completes when the list has been reloaded.</returns>
    private async Task ApplyViewAsync(string view)
    {
        if (LifecycleView == view)
        {
            return;
        }

        LifecycleView = view;
        await LoadTagsAsync();
    }

    /// <summary>
    /// Applies the current search input and reloads the list.
    /// </summary>
    /// <returns>A task that completes when the list has been reloaded.</returns>
    private async Task ApplySearchAsync()
    {
        AppliedSearch = _searchInput.Trim();
        await LoadTagsAsync();
    }

    /// <summary>
    /// Clears the applied search term and reloads the list.
    /// </summary>
    /// <returns>A task that completes when the list has been reloaded.</returns>
    private async Task ClearSearchAsync()
    {
        _searchInput = string.Empty;
        AppliedSearch = string.Empty;
        await LoadTagsAsync();
    }

    /// <summary>
    /// Opens the create form with default values.
    /// </summary>
    private void StartCreate()
    {
        _form = TagFormState.CreateDefault();
        _formError = null;
        _statusMessage = null;
        _showForm = true;
    }

    /// <summary>
    /// Opens the edit form populated from a tag definition.
    /// </summary>
    /// <param name="tag">The tag definition to edit.</param>
    private void StartEdit(TagDefinitionDto tag)
    {
        _form = TagFormState.FromDto(tag);
        _formError = null;
        _statusMessage = null;
        _showForm = true;
    }

    /// <summary>
    /// Closes the create/edit form without mutating data.
    /// </summary>
    private void CancelForm()
    {
        _showForm = false;
        _form = null;
        _formError = null;
    }

    /// <summary>
    /// Creates or updates a tag definition from the submitted form, then reloads the list.
    /// </summary>
    /// <returns>A task that completes when the mutation and reload have finished.</returns>
    private async Task SubmitFormAsync()
    {
        if (_form is null)
        {
            return;
        }

        _isMutating = true;
        _formError = null;
        _statusMessage = null;

        var success = false;

        if (_form.IsEdit)
        {
            ServiceResult<TagDefinitionDto> result;
            try
            {
                result = await managementService.UpdateAsync(_form.ToUpdateInput(), ComponentCancellationToken);
            }
            catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                _ = ex;
                _formError = "Failed to save the tag. Please retry.";
                _isMutating = false;
                return;
            }

            result.Switch(
                updated =>
                {
                    _showForm = false;
                    _form = null;
                    _statusMessage = $"Updated tag \"{updated.Name}\".";
                    success = true;
                },
                problem => HandleFormProblem(problem, "Could not update tag."));
        }
        else
        {
            ServiceResult<TagDefinitionDto> result;
            try
            {
                result = await managementService.CreateAsync(_form.ToCreateInput(), ComponentCancellationToken);
            }
            catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                _ = ex;
                _formError = "Failed to create the tag. Please retry.";
                _isMutating = false;
                return;
            }

            result.Switch(
                created =>
                {
                    _showForm = false;
                    _form = null;
                    _statusMessage = $"Created tag \"{created.Name}\".";
                    success = true;
                },
                problem => HandleFormProblem(problem, "Could not create tag."));
        }

        _isMutating = false;

        if (success)
        {
            await LoadTagsAsync();
        }
    }

    /// <summary>
    /// Opens the archive confirmation panel for a tag definition.
    /// </summary>
    /// <param name="tag">The tag definition to archive.</param>
    private void BeginArchive(TagDefinitionDto tag)
    {
        _archiveTarget = tag;
        _actionError = null;
        _statusMessage = null;
    }

    /// <summary>
    /// Closes the archive confirmation panel without mutating data.
    /// </summary>
    private void CancelArchive()
    {
        _archiveTarget = null;
        _actionError = null;
    }

    /// <summary>
    /// Archives the selected tag definition, then reloads the list.
    /// </summary>
    /// <returns>A task that completes when the mutation and reload have finished.</returns>
    private async Task ConfirmArchiveAsync()
    {
        if (_archiveTarget is null)
        {
            return;
        }

        _isMutating = true;
        _actionError = null;
        _statusMessage = null;

        ServiceResult<Success> result;
        try
        {
            result = await lifecycleService.ArchiveAsync(_archiveTarget.PlayerTagId, ComponentCancellationToken);
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _ = ex;
            _actionError = "Failed to archive the tag. Please retry.";
            _isMutating = false;
            return;
        }

        var success = false;
        var archivedName = _archiveTarget.Name;
        result.Switch(
            _ =>
            {
                _statusMessage = $"Archived tag \"{archivedName}\".";
                _archiveTarget = null;
                success = true;
            },
            problem => HandleActionProblem(problem, "Could not archive tag."));

        _isMutating = false;

        if (success)
        {
            await LoadTagsAsync();
        }
    }

    /// <summary>
    /// Opens the restore confirmation panel for a tag definition.
    /// </summary>
    /// <param name="tag">The tag definition to restore.</param>
    private void BeginRestore(TagDefinitionDto tag)
    {
        _restoreTarget = tag;
        _actionError = null;
        _statusMessage = null;
    }

    /// <summary>
    /// Closes the restore confirmation panel without mutating data.
    /// </summary>
    private void CancelRestore()
    {
        _restoreTarget = null;
        _actionError = null;
    }

    /// <summary>
    /// Restores the selected tag definition, then reloads the list.
    /// </summary>
    /// <returns>A task that completes when the mutation and reload have finished.</returns>
    private async Task ConfirmRestoreAsync()
    {
        if (_restoreTarget is null)
        {
            return;
        }

        _isMutating = true;
        _actionError = null;
        _statusMessage = null;

        ServiceResult<Success> result;
        try
        {
            result = await lifecycleService.RestoreAsync(_restoreTarget.PlayerTagId, ComponentCancellationToken);
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _ = ex;
            _actionError = "Failed to restore the tag. Please retry.";
            _isMutating = false;
            return;
        }

        var success = false;
        var restoredName = _restoreTarget.Name;
        result.Switch(
            _ =>
            {
                _statusMessage = $"Restored tag \"{restoredName}\".";
                _restoreTarget = null;
                success = true;
            },
            problem => HandleActionProblem(problem, "Could not restore tag."));

        _isMutating = false;

        if (success)
        {
            await LoadTagsAsync();
        }
    }

    /// <summary>
    /// Handles a create/update problem by rendering a form-level error or redirecting on access denial.
    /// </summary>
    /// <param name="problem">The service problem.</param>
    /// <param name="fallback">The fallback message when no detail is present.</param>
    private void HandleFormProblem(ServiceProblem problem, string fallback)
    {
        if (problem.Kind == ServiceProblemKind.Forbidden)
        {
            NavigateToAccessDenied();
            return;
        }

        _formError = problem.Detail ?? fallback;
    }

    /// <summary>
    /// Handles an archive/restore problem by rendering a list-level error or redirecting on access denial.
    /// </summary>
    /// <param name="problem">The service problem.</param>
    /// <param name="fallback">The fallback message when no detail is present.</param>
    private void HandleActionProblem(ServiceProblem problem, string fallback)
    {
        if (problem.Kind == ServiceProblemKind.Forbidden)
        {
            NavigateToAccessDenied();
            return;
        }

        _actionError = problem.Detail ?? fallback;
    }

    /// <summary>
    /// Gets the Bootstrap badge CSS class for a tag's lifecycle status.
    /// </summary>
    /// <param name="status">The tag's lifecycle status.</param>
    /// <returns>A Bootstrap badge class string.</returns>
    private static string LifecycleBadgeClass(LifecycleStatus status) => status switch
    {
        LifecycleStatus.Archived => "badge text-bg-secondary",
        _ => "badge text-bg-success"
    };

    /// <summary>
    /// Gets the human-readable label for a tag's lifecycle status.
    /// </summary>
    /// <param name="status">The tag's lifecycle status.</param>
    /// <returns>The lifecycle label.</returns>
    private static string LifecycleLabel(LifecycleStatus status) => status switch
    {
        LifecycleStatus.Archived => "Archived",
        _ => "Active"
    };

    /// <summary>
    /// Navigates to the access-denied page when authorization fails at the service boundary.
    /// </summary>
    private void NavigateToAccessDenied() => navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
}
