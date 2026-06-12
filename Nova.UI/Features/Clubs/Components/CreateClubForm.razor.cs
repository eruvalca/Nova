using Microsoft.AspNetCore.Components;
using Nova.Shared.Clubs;

namespace Nova.UI.Features.Clubs.Components;

/// <summary>
/// A form component for creating a new club. Validates input and calls
/// <see cref="IClubService.CreateClubAsync"/> on submit.
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
    private FormModel _input = new();

    /// <summary>
    /// Whether a submission is currently in progress. Prevents double-submission.
    /// </summary>
    private bool _submitting;

    /// <summary>
    /// A server-side error message to display, or <see langword="null"/> when no error.
    /// </summary>
    private string? _error;

    /// <summary>
    /// Handles valid form submission: calls the club service and invokes <see cref="OnClubCreated"/> on success.
    /// </summary>
    private async Task HandleSubmitAsync()
    {
        _submitting = true;
        _error = null;

        var result = await clubService.CreateClubAsync(
            new CreateClubInput(_input.Name, _input.City, _input.State),
            ComponentCancellationToken);

        result.Switch(
            club =>
            {
                _ = OnClubCreated.InvokeAsync(club);
            },
            problem =>
            {
                _error = problem.Detail ?? "An error occurred creating the club. Please try again.";
            });

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
