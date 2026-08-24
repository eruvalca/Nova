using Cropper.Blazor.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Photos;
using Nova.UI.Shared;

namespace Nova.UI.Features.Clubs.Components;

/// <summary>
/// A form component for creating a new club. Validates input (including the required crest
/// image, optional crop step) and calls <see cref="IClubService.CreateClubAsync"/> on submit.
/// </summary>
/// <param name="clubService">The service for club operations.</param>
/// <param name="canvasExporter">The cropper canvas exporter used to produce the upload bytes.</param>
public partial class CreateClubForm(IClubService clubService, ICropperCanvasExporter canvasExporter)
{
    /// <summary>
    /// Invoked when the club is successfully created. The created <see cref="ClubDto"/> is passed as the argument.
    /// </summary>
    [Parameter]
    public EventCallback<ClubDto> OnClubCreated { get; set; }

    /// <summary>
    /// The form model bound to the create-club input fields.
    /// </summary>
    private readonly FormModel _input = new();

    /// <summary>
    /// Whether a submission is currently in progress. Prevents double-submission.
    /// </summary>
    private bool _submitting;

    /// <summary>
    /// A server-side error message to display, or <see langword="null"/> when no error.
    /// </summary>
    private string? _error;

    /// <summary>
    /// The validation/processing error messages for the crest upload.
    /// </summary>
    private readonly List<string> _crestErrors = [];

    /// <summary>
    /// The selected crest file, or <see langword="null"/> when none is selected.
    /// </summary>
    private IBrowserFile? _crestFile;

    /// <summary>
    /// The data URL preview of the selected crest image, or <see langword="null"/> when none is selected.
    /// </summary>
    private string? _crestPreviewUrl;

    /// <summary>
    /// The cropper component reference used to extract the cropped canvas.
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
    /// The verified bytes of the cropped crest ready for submission, or <see langword="null"/>
    /// when the crop step has not been completed yet.
    /// </summary>
    private byte[]? _croppedCrestContent;

    /// <summary>
    /// Gets a value indicating whether the crop step is active (a file is selected but not yet saved).
    /// </summary>
    private bool IsCropping => _crestFile is not null;

    /// <summary>
    /// Gets a value indicating whether the chosen source image passed validation and can be cropped.
    /// </summary>
    private bool CanSubmit => _crestFile is not null && _crestErrors.Count == 0;

    /// <summary>
    /// Gets the <c>accept</c> attribute value for the crest file input.
    /// </summary>
    private static string AcceptTypes => ProfilePhotoConstraints.AcceptAttribute;

    /// <summary>
    /// Handles crest file selection: validates size/type client-side and shows a preview.
    /// </summary>
    /// <param name="args">The file change event arguments.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task OnCrestSelectedAsync(InputFileChangeEventArgs args)
    {
        _crestErrors.Clear();
        _croppedCrestContent = null;
        var file = args.File;

        if (file.Size is 0 or > ProfilePhotoConstraints.MaxBytes)
        {
            _crestErrors.Add($"The crest must be between 1 byte and {ProfilePhotoConstraints.MaxBytes / (1024 * 1024)} MB.");
            _crestFile = null;
            return;
        }

        if (!ProfilePhotoConstraints.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            _crestErrors.Add("Only JPEG, PNG, and WebP images are allowed.");
            _crestFile = null;
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
        _croppedCrestContent = null;
        _crestErrors.Clear();
    }

    /// <summary>
    /// Exports the cropped canvas and stores the JPEG bytes for submission.
    /// </summary>
    /// <returns>A task that completes when the crop has been exported.</returns>
    private async Task SaveCrestAsync()
    {
        if (_crestFile is null || CanSubmit is false)
        {
            return;
        }

        _submitting = true;
        _crestErrors.Clear();

        byte[] crestContent;
        try
        {
            crestContent = await canvasExporter.ExportAsync(_cropper!, ComponentCancellationToken);
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            _submitting = false;
            return;
        }
        catch (Exception)
        {
            _crestErrors.Add("The cropped image could not be processed. Please try again.");
            _submitting = false;
            return;
        }

        if (crestContent.Length == 0)
        {
            _crestErrors.Add("The cropped image could not be processed. Please try again.");
            _submitting = false;
            return;
        }

        _croppedCrestContent = crestContent;
        _crestFile = null;

        // Keep the preview visible after the crop is saved by building a data URL from the bytes.
        _crestPreviewUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(crestContent)}";

        _submitting = false;
    }

    /// <summary>
    /// Handles valid form submission: calls the club service and invokes <see cref="OnClubCreated"/> on success.
    /// </summary>
    private async Task HandleSubmitAsync()
    {
        _submitting = true;
        _error = null;
        _crestErrors.Clear();

        if (_croppedCrestContent is null)
        {
            _crestErrors.Add("A crest image is required.");
            _submitting = false;
            return;
        }

        byte[] crestContent = _croppedCrestContent;

        var result = await clubService.CreateClubAsync(
            new CreateClubInput
            {
                Name = _input.Name,
                City = _input.City,
                State = _input.State,
                CrestContent = crestContent,
                CrestContentType = "image/jpeg"
            },
            ComponentCancellationToken);

        result.Switch(
            club => _ = OnClubCreated.InvokeAsync(club),
            problem => _error = problem.Detail ?? "An error occurred creating the club. Please try again.");

        _submitting = false;
    }

    /// <summary>
    /// Internal form model with validation annotations for the create-club form.
    /// </summary>
    private sealed class FormModel
    {
        /// <summary>Gets or sets the club name.</summary>
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Club name is required.")]
        [System.ComponentModel.DataAnnotations.MaxLength(100, ErrorMessage = "Club name must be 100 characters or fewer.")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the city.</summary>
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "City is required.")]
        [System.ComponentModel.DataAnnotations.MaxLength(100, ErrorMessage = "City must be 100 characters or fewer.")]
        public string City { get; set; } = string.Empty;

        /// <summary>Gets or sets the state.</summary>
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "State is required.")]
        [System.ComponentModel.DataAnnotations.MaxLength(100, ErrorMessage = "State must be 100 characters or fewer.")]
        public string State { get; set; } = string.Empty;
    }
}
