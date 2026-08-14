using System.Globalization;
using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Tags;
using Nova.Shared.Features.Teams;
using Nova.UI.Features.Players;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the campaign roster filter bar: debounced search, graduation-year and tag multi-selects,
/// outcome and team selects, a conditional clear button, and the participant count.
/// </summary>
public partial class CampaignRosterFilters
{
    /// <summary>
    /// Gets or sets the current search text draft owned by the parent page.
    /// </summary>
    [Parameter, EditorRequired]
    public string SearchText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the graduation years available as filter choices.
    /// </summary>
    [Parameter]
    public IReadOnlyList<int> GraduationYearChoices { get; set; } = [];

    /// <summary>
    /// Gets or sets the selected graduation years.
    /// </summary>
    [Parameter]
    public IReadOnlyCollection<int> SelectedGraduationYears { get; set; } = [];

    /// <summary>
    /// Gets or sets the tag definitions available as filter choices.
    /// </summary>
    [Parameter]
    public IReadOnlyList<TagDefinitionDto> TagChoices { get; set; } = [];

    /// <summary>
    /// Gets or sets the selected tag-definition identifiers.
    /// </summary>
    [Parameter]
    public IReadOnlyCollection<long> SelectedTagDefinitionIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the selected placement-outcome token, or <see langword="null"/> when unfiltered.
    /// </summary>
    [Parameter]
    public string? Outcome { get; set; }

    /// <summary>
    /// Gets or sets the teams available as filter choices.
    /// </summary>
    [Parameter]
    public IReadOnlyList<TeamRosterItem> TeamChoices { get; set; } = [];

    /// <summary>
    /// Gets or sets the selected team identifier, or <see langword="null"/> when unfiltered.
    /// </summary>
    [Parameter]
    public long? TeamId { get; set; }

    /// <summary>
    /// Gets or sets the total matching participant count, or <see langword="null"/> while loading.
    /// </summary>
    [Parameter]
    public int? TotalCount { get; set; }

    /// <summary>
    /// Gets or sets whether any roster filter is active, controlling the clear button visibility.
    /// </summary>
    [Parameter]
    public bool HasActiveFilters { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked on every search input change with the raw draft text.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnSearchTextChanged { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a graduation-year choice is toggled.
    /// </summary>
    [Parameter]
    public EventCallback<(int Year, bool Selected)> OnGraduationYearToggled { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a tag choice is toggled.
    /// </summary>
    [Parameter]
    public EventCallback<(long PlayerTagId, bool Selected)> OnTagToggled { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the outcome select changes, with the selected token or an empty string.
    /// </summary>
    [Parameter]
    public EventCallback<string> OnOutcomeChanged { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the team select changes, with the selected identifier or <see langword="null"/>.
    /// </summary>
    [Parameter]
    public EventCallback<long?> OnTeamChanged { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the clear-filters button is clicked.
    /// </summary>
    [Parameter]
    public EventCallback OnClearFilters { get; set; }

    /// <summary>
    /// Gets the selected team identifier as a select-binding string.
    /// </summary>
    private string TeamText => TeamId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Forwards a search input change to the parent page.
    /// </summary>
    /// <param name="args">The input event payload.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnSearchInputAsync(ChangeEventArgs args)
        => OnSearchTextChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty);

    /// <summary>
    /// Forwards a graduation-year toggle to the parent page.
    /// </summary>
    /// <param name="year">The toggled graduation year.</param>
    /// <param name="args">The checkbox change payload.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnGraduationYearToggleAsync(int year, ChangeEventArgs args)
        => OnGraduationYearToggled.InvokeAsync((year, args.Value is true));

    /// <summary>
    /// Forwards a tag toggle to the parent page.
    /// </summary>
    /// <param name="playerTagId">The toggled tag-definition identifier.</param>
    /// <param name="args">The checkbox change payload.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnTagToggleAsync(long playerTagId, ChangeEventArgs args)
        => OnTagToggled.InvokeAsync((playerTagId, args.Value is true));

    /// <summary>
    /// Forwards an outcome select change to the parent page.
    /// </summary>
    /// <param name="args">The select change payload.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnOutcomeChangeAsync(ChangeEventArgs args)
        => OnOutcomeChanged.InvokeAsync(args.Value?.ToString() ?? string.Empty);

    /// <summary>
    /// Forwards a team select change to the parent page.
    /// </summary>
    /// <param name="args">The select change payload.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnTeamChangeAsync(ChangeEventArgs args)
    {
        var raw = args.Value?.ToString();
        var teamId = long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId)
            ? parsedId
            : (long?)null;

        return OnTeamChanged.InvokeAsync(teamId);
    }

    /// <summary>
    /// Forwards a clear-filters click to the parent page.
    /// </summary>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task ClearFiltersAsync() => OnClearFilters.InvokeAsync();

    /// <summary>
    /// Builds a safe inline swatch style for a tag color.
    /// </summary>
    /// <param name="color">The tag color token.</param>
    /// <returns>The sanitized background-color style.</returns>
    private static string BuildSwatchStyle(string color)
        => $"background-color: {PlayerTagStyle.NormalizeColor(color)};";
}
