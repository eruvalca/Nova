using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components;
using Nova.Shared.Clubs;

namespace Nova.UI.Features.Clubs.Components;

/// <summary>
/// A panel for searching clubs by name, city, or state and submitting join requests.
/// </summary>
/// <param name="clubService">The service for club search operations.</param>
/// <param name="clubJoinRequestService">The service for club join request operations.</param>
public partial class ClubSearchPanel(IClubService clubService, IClubJoinRequestService clubJoinRequestService)
{
    /// <summary>
    /// Invoked when a join request is successfully submitted. The created <see cref="ClubJoinRequestDto"/> is passed as the argument.
    /// </summary>
    [Parameter]
    public EventCallback<ClubJoinRequestDto> OnJoinRequested { get; set; }

    /// <summary>
    /// The current search query entered by the user.
    /// </summary>
    private string _query = string.Empty;

    /// <summary>
    /// Whether a search is currently in progress.
    /// </summary>
    private bool _searching;

    /// <summary>
    /// Whether a search has been executed at least once.
    /// </summary>
    private bool _searched;

    /// <summary>
    /// The current list of search results.
    /// </summary>
    private IReadOnlyList<ClubDto> _results = [];

    /// <summary>
    /// The id of the club whose "Request to Join" button is currently in a loading state,
    /// or <see langword="null"/> when no request is in progress.
    /// </summary>
    private long? _requestingClubId;

    /// <summary>
    /// A panel-level error message, or <see langword="null"/> when no error.
    /// </summary>
    private string? _error;

    /// <summary>
    /// Cancellation source for the in-flight debounce delay, or <see langword="null"/> when none is pending.
    /// </summary>
    private CancellationTokenSource? _debounceCts;

    /// <summary>
    /// The minimum number of characters required before an automatic (debounced) search runs.
    /// </summary>
    private const int MinAutoSearchLength = 3;

    /// <summary>
    /// Executes the search by calling <see cref="IClubService.SearchClubsAsync"/>.
    /// </summary>
    private async Task SearchAsync()
    {
        _searching = true;
        _error = null;

        var result = await clubService.SearchClubsAsync(_query, ComponentCancellationToken);
        result.Switch(
            clubs =>
            {
                _results = clubs;
                _searched = true;
            },
            problem => _error = problem.Detail ?? "Failed to search clubs. Please try again.");

        _searching = false;
    }

    /// <summary>
    /// Handles keyboard input: triggers a search when the Enter key is pressed.
    /// </summary>
    /// <param name="args">The keyboard event arguments.</param>
    private async Task HandleKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await SearchAsync();
        }
    }

    /// <summary>
    /// Handles text input: updates the query and triggers a debounced automatic search once the
    /// query reaches <see cref="MinAutoSearchLength"/> characters. Shorter queries cancel any
    /// pending search and clear prior results.
    /// </summary>
    /// <param name="args">The input change event arguments.</param>
    private async Task HandleInputAsync(ChangeEventArgs args)
    {
        _query = args.Value?.ToString() ?? string.Empty;

        // Cancel any pending debounce.
        if (_debounceCts is not null)
        {
            await _debounceCts.CancelAsync();
            _debounceCts.Dispose();
            _debounceCts = null;
        }

        if (_query.Length < MinAutoSearchLength)
        {
            // Below threshold: clear any previous results so stale matches do not linger.
            if (_results.Count > 0 || _searched)
            {
                _results = [];
                _searched = false;
            }
            return;
        }

        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        try
        {
            await Task.Delay(300, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            await SearchAsync();
        }
    }

    /// <summary>
    /// Submits a join request for the specified club.
    /// </summary>
    /// <param name="club">The club to request joining.</param>
    private async Task RequestJoinAsync(ClubDto club)
    {
        _requestingClubId = club.ClubId;
        _error = null;

        var result = await clubJoinRequestService.CreateJoinRequestAsync(club.ClubId, ComponentCancellationToken);
        result.Switch(
            dto => _ = OnJoinRequested.InvokeAsync(dto),
            problem => _error = problem.Detail ?? "Failed to submit join request. Please try again.");

        _requestingClubId = null;
    }

    /// <inheritdoc />
    protected override ValueTask DisposeAsyncCore()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
        return ValueTask.CompletedTask;
    }
}
