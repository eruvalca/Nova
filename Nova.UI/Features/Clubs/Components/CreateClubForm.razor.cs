using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Photos;

namespace Nova.UI.Features.Clubs.Components;

/// <summary>
/// A form component for creating a new club. Validates input (including the required crest
/// image) and calls <see cref="IClubService.CreateClubAsync"/> on submit.
/// </summary>
/// <param name="clubService">The service for club operations.</param>
public partial class CreateClubForm(IClubService clubService)
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
    /// The selected crest content type, or <see langword="null"/> when none is selected.
    /// </summary>
    private string? _crestContentType;

    /// <summary>
    /// The data URL preview of the selected crest image, or <see langword="null"/> when none is selected.
    /// </summary>
    private string? _crestPreviewUrl;

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
            _crestContentType = file.ContentType;
        }
        catch (IOException)
        {
            _crestErrors.Add("The crest could not be read. Please try a different file.");
            _crestFile = null;
        }
    }

    /// <summary>
    /// Handles valid form submission: calls the club service and invokes <see cref="OnClubCreated"/> on success.
    /// </summary>
    private async Task HandleSubmitAsync()
    {
        _submitting = true;
        _error = null;
        _crestErrors.Clear();

        if (_crestFile is null)
        {
            _crestErrors.Add("A crest image is required.");
            _submitting = false;
            return;
        }

        if (_crestFile.Size is 0 or > ProfilePhotoConstraints.MaxBytes)
        {
            _crestErrors.Add($"The crest must be between 1 byte and {ProfilePhotoConstraints.MaxBytes / (1024 * 1024)} MB.");
            _submitting = false;
            return;
        }

        byte[] crestContent;
        try
        {
            await using var stream = _crestFile.OpenReadStream(ProfilePhotoConstraints.MaxBytes, ComponentCancellationToken);
            using var buffer = new MemoryStream((int)_crestFile.Size);
            await stream.CopyToAsync(buffer, ComponentCancellationToken);
            crestContent = buffer.ToArray();
        }
        catch (IOException)
        {
            _crestErrors.Add("The crest could not be read. Please try a different file.");
            _submitting = false;
            return;
        }

        var result = await clubService.CreateClubAsync(
            new CreateClubInput
            {
                Name = _input.Name,
                City = _input.City,
                State = _input.State,
                CrestContent = crestContent,
                CrestContentType = _crestContentType ?? "application/octet-stream"
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
