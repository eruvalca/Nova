using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.Shared.Validation;
using Nova.UI.Components;
using Nova.UI.Features.Players;

namespace Nova.UI.Features.Tags.Components;

/// <summary>
/// Interactive club-admin panel for managing club tag definitions. Provides create, edit,
/// archive, and restore flows against the tag-definition management API.
/// </summary>
public partial class TagDefinitionManagementPanel(ITagDefinitionService tagDefinitionService, NavigationManager navigationManager) : NovaComponentBase
{
    private const int MaxTags = 100;

    private readonly CreateTagFormModel _createForm = new();

    private readonly UpdateTagFormModel _editForm = new();

    private bool _loading = true;
    private bool _submitting;
    private bool _showCreateForm;
    private long? _editingTagId;
    private long? _confirmingArchiveId;
    private long? _confirmingRestoreId;
    private string? _status;
    private string? _mutationError;

    /// <summary>
    /// The active tag definitions for the current club.
    /// Persisted across prerender to interactive attach to avoid a duplicate initial fetch.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<TagDefinitionSummary>? Active { get; set; }

    /// <summary>
    /// The archived tag definitions for the current club.
    /// Persisted across prerender to interactive attach to avoid a duplicate initial fetch.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<TagDefinitionSummary>? Archived { get; set; }

    /// <summary>
    /// The initial-load error message shown when tag definitions cannot be fetched.
    /// Persisted across prerender to interactive attach.
    /// </summary>
    [PersistentState]
    public string? LoadError { get; set; }

    /// <summary>
    /// Whether the initial lists have already been loaded during prerendering.
    /// Persisted to prevent duplicate API calls after hydration.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <summary>
    /// Builds the inline badge style for a tag definition.
    /// </summary>
    /// <param name="tag">The tag definition to style.</param>
    /// <returns>A sanitized inline style string.</returns>
    private static string TagBadgeStyle(TagDefinitionSummary tag) => PlayerTagStyle.BuildBadgeStyle(tag.Color);

    /// <summary>
    /// Gets the create-form color preview style, updating as the user types.
    /// </summary>
    private string CreateColorPreviewStyle => PlayerTagStyle.BuildBadgeStyle(_createForm.Color);

    /// <summary>
    /// Gets the edit-form color preview style, updating as the user types.
    /// </summary>
    private string EditColorPreviewStyle => PlayerTagStyle.BuildBadgeStyle(_editForm.Color);

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        if (Initialized)
        {
            _loading = false;
            return;
        }

        await LoadAsync();
        Initialized = true;
        _loading = false;
    }

    /// <summary>Reloads the active and archived lists without clearing mutation feedback.</summary>
    /// <returns>A task that completes when both lists have loaded.</returns>
    private async Task LoadAsync()
    {
        var activeResult = await tagDefinitionService.GetActiveAsync(new GetTagDefinitionsInput { Limit = MaxTags }, ComponentCancellationToken);
        activeResult.Switch(
            active => Active = active,
            problem =>
            {
                if (NavigateWhenForbidden(problem))
                {
                    return;
                }

                LoadError = problem.Detail ?? "Failed to load tag definitions.";
            });

        var archivedResult = await tagDefinitionService.GetArchivedAsync(new GetTagDefinitionsInput { Limit = MaxTags }, ComponentCancellationToken);
        archivedResult.Switch(
            archived => Archived = archived,
            problem =>
            {
                if (NavigateWhenForbidden(problem))
                {
                    return;
                }

                LoadError = problem.Detail ?? "Failed to load archived tag definitions.";
            });
    }

    /// <summary>Opens the create form, closing any other open mutation surface.</summary>
    private void ShowCreateForm()
    {
        _showCreateForm = true;
        _editingTagId = null;
        _confirmingArchiveId = null;
        _confirmingRestoreId = null;
        _mutationError = null;
    }

    /// <summary>Closes the create form.</summary>
    private void CancelCreateForm() => _showCreateForm = false;

    /// <summary>Opens the edit form for a tag definition, closing any other open mutation surface.</summary>
    /// <param name="tag">The tag definition to edit.</param>
    private void BeginEdit(TagDefinitionSummary tag)
    {
        _editingTagId = tag.TagDefinitionId;
        _editForm.Name = tag.Name;
        _editForm.Color = tag.Color;
        _showCreateForm = false;
        _confirmingArchiveId = null;
        _confirmingRestoreId = null;
        _mutationError = null;
    }

    /// <summary>Closes the edit form.</summary>
    private void CancelEdit() => _editingTagId = null;

    /// <summary>Starts the archive confirmation flow for a tag definition.</summary>
    /// <param name="tag">The tag definition to archive.</param>
    private void BeginArchive(TagDefinitionSummary tag)
    {
        _confirmingArchiveId = tag.TagDefinitionId;
        _editingTagId = null;
        _showCreateForm = false;
        _confirmingRestoreId = null;
        _mutationError = null;
    }

    /// <summary>Cancels the archive confirmation flow.</summary>
    private void CancelArchive() => _confirmingArchiveId = null;

    /// <summary>Starts the restore confirmation flow for an archived tag definition.</summary>
    /// <param name="tag">The tag definition to restore.</param>
    private void BeginRestore(TagDefinitionSummary tag)
    {
        _confirmingRestoreId = tag.TagDefinitionId;
        _confirmingArchiveId = null;
        _editingTagId = null;
        _showCreateForm = false;
        _mutationError = null;
    }

    /// <summary>Cancels the restore confirmation flow.</summary>
    private void CancelRestore() => _confirmingRestoreId = null;

    /// <summary>Creates a tag definition from the create form.</summary>
    /// <returns>A task that completes when the create attempt finishes.</returns>
    private async Task HandleCreateAsync()
    {
        _submitting = true;
        _status = null;
        _mutationError = null;

        var shouldReturn = false;
        var result = await tagDefinitionService.CreateAsync(
            new CreateTagDefinitionInput { Name = _createForm.Name.Trim(), Color = _createForm.Color },
            ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _status = $"Tag definition \"{_createForm.Name.Trim()}\" created.";
                _showCreateForm = false;
                _createForm.Reset();
            },
            problem =>
            {
                if (NavigateWhenForbidden(problem))
                {
                    shouldReturn = true;
                    return;
                }

                _mutationError = problem.Detail ?? "Failed to create the tag definition. Please try again.";
            });

        _submitting = false;
        if (shouldReturn)
        {
            return;
        }

        await LoadAsync();
    }

    /// <summary>Updates the tag definition currently being edited.</summary>
    /// <returns>A task that completes when the update attempt finishes.</returns>
    private async Task HandleUpdateAsync()
    {
        if (_editingTagId is null)
        {
            return;
        }

        _submitting = true;
        _status = null;
        _mutationError = null;

        var tagId = _editingTagId.Value;
        var shouldReturn = false;
        var result = await tagDefinitionService.UpdateAsync(
            new UpdateTagDefinitionInput { TagDefinitionId = tagId, Name = _editForm.Name.Trim(), Color = _editForm.Color },
            ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _status = $"Tag definition \"{_editForm.Name.Trim()}\" updated.";
                _editingTagId = null;
            },
            problem =>
            {
                if (NavigateWhenForbidden(problem))
                {
                    shouldReturn = true;
                    return;
                }

                _mutationError = problem.Detail ?? "Failed to update the tag definition. Please try again.";
            });

        _submitting = false;
        if (shouldReturn)
        {
            return;
        }

        await LoadAsync();
    }

    /// <summary>Archives the tag definition currently awaiting confirmation.</summary>
    /// <returns>A task that completes when the archive attempt finishes.</returns>
    private async Task ConfirmArchiveAsync()
    {
        if (_confirmingArchiveId is null)
        {
            return;
        }

        _submitting = true;
        _status = null;
        _mutationError = null;

        var tagId = _confirmingArchiveId.Value;
        var shouldReturn = false;
        var result = await tagDefinitionService.ArchiveAsync(tagId, ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _status = "Tag definition archived.";
                _confirmingArchiveId = null;
            },
            problem =>
            {
                if (NavigateWhenForbidden(problem))
                {
                    shouldReturn = true;
                    return;
                }

                _mutationError = problem.Detail ?? "Failed to archive the tag definition. Please try again.";
            });

        _submitting = false;
        if (shouldReturn)
        {
            return;
        }

        await LoadAsync();
    }

    /// <summary>Restores the archived tag definition currently awaiting confirmation.</summary>
    /// <returns>A task that completes when the restore attempt finishes.</returns>
    private async Task ConfirmRestoreAsync()
    {
        if (_confirmingRestoreId is null)
        {
            return;
        }

        _submitting = true;
        _status = null;
        _mutationError = null;

        var tagId = _confirmingRestoreId.Value;
        var shouldReturn = false;
        var result = await tagDefinitionService.RestoreAsync(tagId, ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _status = "Tag definition restored.";
                _confirmingRestoreId = null;
            },
            problem =>
            {
                if (NavigateWhenForbidden(problem))
                {
                    shouldReturn = true;
                    return;
                }

                _mutationError = problem.Detail ?? "Failed to restore the tag definition. Please try again.";
            });

        _submitting = false;
        if (shouldReturn)
        {
            return;
        }

        await LoadAsync();
    }

    /// <summary>
    /// Navigates to the access-denied page when the problem is a forbidden result.
    /// </summary>
    /// <param name="problem">The service problem to inspect.</param>
    /// <returns><see langword="true"/> when the problem was forbidden and navigation started; otherwise <see langword="false"/>.</returns>
    private bool NavigateWhenForbidden(ServiceProblem problem)
    {
        if (problem.Kind == ServiceProblemKind.Forbidden)
        {
            navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Mutable form model for creating a tag definition, mirroring the shared create contract
    /// so the interactive form can bind and validate before submission.
    /// </summary>
    private sealed class CreateTagFormModel
    {
        [Required]
        [NotWhitespace]
        [StringLength(80, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color must be a hex value in the format #RRGGBB.")]
        public string Color { get; set; } = "#4F46E5";

        /// <summary>Resets the form to its default values after a successful create.</summary>
        public void Reset()
        {
            Name = string.Empty;
            Color = "#4F46E5";
        }
    }

    /// <summary>
    /// Mutable form model for updating a tag definition, mirroring the shared update contract
    /// so the interactive form can bind and validate before submission.
    /// </summary>
    private sealed class UpdateTagFormModel
    {
        [Required]
        [NotWhitespace]
        [StringLength(80, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color must be a hex value in the format #RRGGBB.")]
        public string Color { get; set; } = "#4F46E5";
    }
}
