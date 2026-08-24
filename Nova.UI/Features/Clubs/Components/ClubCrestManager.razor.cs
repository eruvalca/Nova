using Cropper.Blazor.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Photos;
using Nova.Shared.Results;
using Nova.UI.Components;
using Nova.UI.Shared;
using OneOf.Types;

namespace Nova.UI.Features.Clubs.Components;

/// <summary>
/// Interactive island that lets club administrators change or remove the club crest. The current
/// presence of a crest (<see cref="HasCrest"/>) is supplied by the server-rendered host page and
/// tracked locally once the island becomes interactive so mutations do not revert the preview.
/// After a file is selected the user can optionally crop it (free-form) before it is uploaded as
/// JPEG through the shared cropper export path.
/// </summary>
/// <param name="clubCrestService">The club crest change/remove service.</param>
/// <param name="canvasExporter">The cropper canvas exporter used to produce the upload bytes.</param>
/// <param name="navigationManager">The navigation manager used for access-denied redirects.</param>
public partial class ClubCrestManager(
    IClubCrestService clubCrestService,
    ICropperCanvasExporter canvasExporter,
    NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// Gets or sets the id of the club whose crest is managed. Supplied by the host page.
    /// </summary>
    [Parameter]
    public long ClubId { get; set; }

    /// <summary>
    /// Gets or sets whether the club currently has a crest at initial render.
    /// </summary>
    [Parameter]
    public bool HasCrest { get; set; }

    /// <summary>
    /// Gets or sets whether the initial crest presence has been captured, persisted across
    /// prerender and interactive attach so the locally mutated state is not overwritten by the
    /// (stale) parameter on re-attach.
    /// </summary>
    [PersistentState]
    public bool HasCrestInitialized { get; set; }

    /// <summary>
    /// Gets or sets the locally tracked crest presence used for rendering.
    /// </summary>
    [PersistentState]
    public bool CrestPresent { get; set; }

    /// <summary>
    /// Gets or sets the crest file currently selected for upload, or <see langword="null"/> when none.
    /// </summary>
    private IBrowserFile? _crestFile;

    /// <summary>
    /// Gets or sets the data URL preview of the selected crest image.
    /// </summary>
    private string? _crestPreviewUrl;

    /// <summary>
    /// Gets or sets the validation/processing error messages for the crest upload.
    /// </summary>
    private readonly List<string> _crestErrors = [];

    /// <summary>
    /// Gets or sets the last action error message, or <see langword="null"/> when no error occurred.
    /// </summary>
    private string? _actionError;

    /// <summary>
    /// Gets or sets the last success message, or <see langword="null"/> when no message is shown.
    /// </summary>
    private string? _statusMessage;

    /// <summary>
    /// Gets or sets whether a mutation is currently in progress. Prevents double-submission.
    /// </summary>
    private bool _isMutating;

    /// <summary>
    /// Gets or sets whether the remove confirmation panel is visible.
    /// </summary>
    private bool _confirmRemove;

    /// <summary>
    /// Gets or sets whether the cropper crop step is active (a file is selected but not yet saved).
    /// </summary>
    private bool IsCropping => _crestFile is not null;

    /// <summary>
    /// Gets or sets the cropper component reference used to extract the cropped canvas.
    /// </summary>
    private Cropper.Blazor.Components.CropperComponent? _cropper;

    /// <summary>
    /// The cropper options: free-form crop (no fixed aspect ratio) with the full image
    /// pre-selected as the crop area, matching the profile-photo cropper behavior.
    /// </summary>
    private readonly Options _cropperOptions = new()
    {
        ViewMode = ViewMode.Vm1,
        AutoCrop = true,
        AutoCropArea = 1m,
    };

    /// <summary>
    /// Gets a value indicating whether a validated crest file is selected and ready to crop/upload.
    /// </summary>
    private bool CanSubmit => _crestFile is not null && _crestErrors.Count == 0;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        if (!HasCrestInitialized)
        {
            CrestPresent = HasCrest;
            HasCrestInitialized = true;
        }
    }

    /// <summary>
    /// Handles crest file selection: validates size/type client-side and enters the crop step
    /// with the selected image loaded into the cropper.
    /// </summary>
    /// <param name="args">The file change event arguments.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task OnCrestSelectedAsync(InputFileChangeEventArgs args)
    {
        _crestErrors.Clear();
        _actionError = null;
        _statusMessage = null;

        var file = args.File;

        if (file.Size is 0 or > ProfilePhotoConstraints.MaxBytes)
        {
            _crestErrors.Add($"The crest must be between 1 byte and {ProfilePhotoConstraints.MaxBytes / (1024 * 1024)} MB.");
            _crestFile = null;
            _crestPreviewUrl = null;
            return;
        }

        if (!ProfilePhotoConstraints.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            _crestErrors.Add("Only JPEG, PNG, and WebP images are allowed.");
            _crestFile = null;
            _crestPreviewUrl = null;
            return;
        }

        try
        {
            await using var stream = file.OpenReadStream(ProfilePhotoConstraints.MaxBytes, ComponentCancellationToken);
            using var buffer = new MemoryStream((int)file.Size);
            await stream.CopyToAsync(buffer, ComponentCancellationToken);
            _crestPreviewUrl = $"data:{file.ContentType};base64,{Convert.ToBase64String(buffer.ToArray())}";
            _crestFile = file;
        }
        catch (IOException)
        {
            _crestErrors.Add("The crest could not be read. Please try a different file.");
            _crestFile = null;
            _crestPreviewUrl = null;
        }
    }

    /// <summary>
    /// Clears the selected crest file, its preview, and the crop state.
    /// </summary>
    private void ClearSelection()
    {
        _crestFile = null;
        _crestPreviewUrl = null;
        _cropper = null;
        _crestErrors.Clear();
        _actionError = null;
        _statusMessage = null;
    }

    /// <summary>
    /// Exports the cropped canvas and uploads the JPEG bytes, replacing any existing crest.
    /// </summary>
    /// <returns>A task that completes when the mutation has finished.</returns>
    private async Task SaveCrestAsync()
    {
        if (_crestFile is null)
        {
            return;
        }

        if (CanSubmit is false)
        {
            return;
        }

        _isMutating = true;
        _actionError = null;
        _statusMessage = null;
        _crestErrors.Clear();

        byte[] crestContent;
        try
        {
            crestContent = await canvasExporter.ExportAsync(_cropper!, ComponentCancellationToken);
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            _isMutating = false;
            return;
        }
        catch (Exception)
        {
            _crestErrors.Add("The cropped image could not be processed. Please try again.");
            _isMutating = false;
            return;
        }

        if (crestContent.Length == 0)
        {
            _crestErrors.Add("The cropped image could not be processed. Please try again.");
            _isMutating = false;
            return;
        }

        ServiceResult<Success> result;
        try
        {
            result = await clubCrestService.ChangeClubCrestAsync(
                ClubId,
                new ClubCrestUpload(crestContent, "image/jpeg"),
                ComponentCancellationToken);
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            _isMutating = false;
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _ = ex;
            _actionError = "Failed to update the crest. Please retry.";
            _isMutating = false;
            return;
        }

        result.Switch(
            _ =>
            {
                ClearSelection();
                CrestPresent = true;
                _statusMessage = "Club crest updated.";
            },
            problem => HandleActionProblem(problem, "Could not update the club crest."));

        _isMutating = false;
    }

    /// <summary>
    /// Opens the remove confirmation panel.
    /// </summary>
    private void BeginRemove()
    {
        _confirmRemove = true;
        _actionError = null;
        _statusMessage = null;
    }

    /// <summary>
    /// Closes the remove confirmation panel without mutating data.
    /// </summary>
    private void CancelRemove()
    {
        _confirmRemove = false;
        _actionError = null;
    }

    /// <summary>
    /// Removes the club crest after the admin confirms.
    /// </summary>
    /// <returns>A task that completes when the mutation has finished.</returns>
    private async Task ConfirmRemoveAsync()
    {
        _isMutating = true;
        _actionError = null;
        _statusMessage = null;

        ServiceResult<Success> result;
        try
        {
            result = await clubCrestService.RemoveClubCrestAsync(ClubId, ComponentCancellationToken);
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            _isMutating = false;
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _ = ex;
            _actionError = "Failed to remove the crest. Please retry.";
            _isMutating = false;
            return;
        }

        result.Switch(
            _ =>
            {
                _confirmRemove = false;
                ClearSelection();
                CrestPresent = false;
                _statusMessage = "Club crest removed.";
            },
            problem => HandleActionProblem(problem, "Could not remove the club crest."));

        _isMutating = false;
    }

    /// <summary>
    /// Handles a mutation problem by rendering an action-level error or redirecting on access denial.
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

        _actionError = FirstNonBlank(problem.Detail, FlattenValidationErrors(problem), fallback);
    }

    /// <summary>
    /// Returns the first non-blank candidate, used to prefer detail text over field-level validation messages.
    /// </summary>
    /// <param name="candidates">The ordered candidate messages.</param>
    /// <returns>The first non-blank message.</returns>
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
    /// Navigates to the access-denied page when authorization fails at the service boundary.
    /// </summary>
    private void NavigateToAccessDenied() => navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
}
