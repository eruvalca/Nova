using Cropper.Blazor.Components;
using Cropper.Blazor.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Nova.Shared.Photos;
using Nova.UI.Components;

namespace Nova.UI.Features.Account.Components;

/// <summary>
/// Lets the user pick a photo, crop it to a circle, and save it as their profile photo.
/// The crop box shows a circular overlay directly in the cropper. On success, performs a
/// full-document navigation to the cookie-refresh endpoint so the new profile photo claim
/// takes effect.
/// </summary>
/// <param name="photoService">The profile photo service (server-direct or HTTP depending on render location).</param>
/// <param name="navigationManager">The navigation manager.</param>
public partial class ProfilePhotoEditor(IProfilePhotoService photoService, NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// The cropper component reference used to extract the cropped canvas.
    /// </summary>
    private CropperComponent? cropper;

    /// <summary>
    /// The validation/processing error messages currently displayed.
    /// </summary>
    private readonly List<string> errorMessages = [];

    /// <summary>
    /// The cropper options: square aspect ratio, circular crop indicator.
    /// </summary>
    private readonly Options cropperOptions = new()
    {
        AspectRatio = 1m,
        ViewMode = ViewMode.Vm1,
    };

    /// <summary>
    /// Gets or sets the local URL to return to after the photo is saved.
    /// </summary>
    [Parameter]
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Gets or sets the local URL to navigate to when the user cancels the operation.
    /// When <see langword="null"/> or empty, no cancel button is shown.
    /// </summary>
    [Parameter]
    public string? CancelUrl { get; set; }

    /// <summary>
    /// Gets the data URL of the selected source image, or <see langword="null"/> when no image is selected.
    /// </summary>
    private string? ImageDataUrl { get; set; }

    /// <summary>
    /// Gets the URL of the user's existing photo, or <see langword="null"/> when the user has none.
    /// </summary>
    private string? ExistingPhotoUrl { get; set; }

    /// <summary>
    /// Gets a value indicating whether a file read or save operation is in progress.
    /// </summary>
    private bool IsBusy { get; set; }

    /// <summary>
    /// Gets the <c>accept</c> attribute value for the file input.
    /// </summary>
    private static string AcceptTypes => ProfilePhotoConstraints.AcceptAttribute;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var result = await photoService.GetCurrentUserPhotoAsync(ComponentCancellationToken);
        ExistingPhotoUrl = result.Match<string?>(
            info => PhotoEndpoints.GetPhotoUrl(info.NovaUserId, ProfilePhotoSize.Medium),
            problem => null);
    }

    /// <summary>
    /// Handles file selection: validates size/type client-side and loads the image into the cropper.
    /// </summary>
    /// <param name="args">The file change event arguments.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task OnFileSelectedAsync(InputFileChangeEventArgs args)
    {
        errorMessages.Clear();
        var file = args.File;

        if (file.Size > ProfilePhotoConstraints.MaxBytes)
        {
            errorMessages.Add("The photo exceeds the maximum allowed size of 10 MB.");
            return;
        }

        if (!ProfilePhotoConstraints.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            errorMessages.Add("Only JPEG, PNG, and WebP images are allowed.");
            return;
        }

        IsBusy = true;
        try
        {
            await using var stream = file.OpenReadStream(ProfilePhotoConstraints.MaxBytes, ComponentCancellationToken);
            using var buffer = new MemoryStream((int)file.Size);
            await stream.CopyToAsync(buffer, ComponentCancellationToken);
            ImageDataUrl = $"data:{file.ContentType};base64,{Convert.ToBase64String(buffer.ToArray())}";
        }
        catch (IOException)
        {
            errorMessages.Add("The photo could not be read. Please try a different file.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Extracts the cropped square image from the cropper, saves it through the photo service,
    /// and navigates (full document load) to the cookie-refresh endpoint on success.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    private async Task SavePhotoAsync()
    {
        if (cropper is null)
        {
            return;
        }

        errorMessages.Clear();
        IsBusy = true;
        try
        {
            // Export as JPEG (universally supported by canvas) on a white background so
            // transparent source regions don't turn black. The background transfer streams
            // the image in chunks, which keeps SignalR messages small on server circuits.
            var imageReceiver = await cropper.GetCroppedCanvasDataInBackgroundAsync(
                new GetCroppedCanvasOptions
                {
                    MaxWidth = ProfilePhotoConstraints.LargeSize,
                    MaxHeight = ProfilePhotoConstraints.LargeSize,
                    FillColor = "#ffffff",
                    ImageSmoothingQuality = "high"
                },
                "image/jpeg",
                0.9f,
                maximumReceiveChunkSize: null,
                ComponentCancellationToken);

            byte[] content;
            using (var imageStream = await imageReceiver.GetImageChunkStreamAsync(ComponentCancellationToken))
            {
                content = imageStream.ToArray();
            }

            if (content.Length == 0)
            {
                errorMessages.Add("The cropped image could not be processed. Please try again.");
                return;
            }

            var result = await photoService.SaveProfilePhotoAsync(
                new ProfilePhotoUpload(content, "image/jpeg", "profile-photo.jpg"),
                ComponentCancellationToken);

            if (result.IsSuccess)
            {
                var returnUrl = string.IsNullOrEmpty(ReturnUrl) ? "/" : ReturnUrl;
                var completeUrl = $"{PhotoEndpoints.Complete}?returnUrl={Uri.EscapeDataString(returnUrl)}";

                // Full document load so the server reissues the auth cookie with the new claim.
                navigationManager.NavigateTo(completeUrl, forceLoad: true);
            }
            else
            {
                var problem = result.Problem;
                if (problem.Errors is { Count: > 0 })
                {
                    errorMessages.AddRange(problem.Errors.Values.SelectMany(messages => messages));
                }
                else if (!string.IsNullOrEmpty(problem.Detail))
                {
                    errorMessages.Add(problem.Detail);
                }
                else
                {
                    errorMessages.Add("The photo could not be saved. Please try again.");
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Clears the current selection so the user can pick a different file.
    /// </summary>
    private void ChooseDifferentPhoto()
    {
        errorMessages.Clear();
        ImageDataUrl = null;
    }

    /// <summary>
    /// Navigates to the cancel URL to close the photo editor without saving.
    /// </summary>
    private void CancelPhoto() => navigationManager.NavigateTo(CancelUrl ?? "/");
}
