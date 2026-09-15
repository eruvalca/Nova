
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Features.Teams;
using Nova.UI.Features.Players;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the campaign roster filter bar: debounced search, graduation-year and tag multi-selects,
/// outcome, local team and effective eligibility choices.
/// </summary>
public partial class CampaignRosterFilters
{
    private bool _expanded = true;

    /// <summary>Uses a collapsed, stacked filter shelf for a narrow working queue.</summary>
    [Parameter] public bool Compact { get; set; }

    /// <inheritdoc />
    protected override void OnInitialized() => _expanded = !Compact;

    /// <summary>
    /// Gets or sets a value indicating whether every discovery control is unavailable.
    /// </summary>
    /// <remarks>
    /// Callers that raise a navigation from these controls disable them while a read is in flight: their
    /// handlers derive the next state from the applied one, which does not advance until that read returns, so
    /// two quick changes would let the second drop the first.
    /// </remarks>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether Active eligibility discovery is available.</summary>
    [Parameter] public bool ShowEligibility { get; set; }
    /// <summary>The selected Active work eligibility.</summary>
    [Parameter] public string? Eligibility { get; set; }
    /// <summary>Whole-campaign work counts, independent of the current discovery page.</summary>
    [Parameter] public EffectivePlacementCounts? EligibilityCounts { get; set; }
    private static string CountSuffix(int? count) => count is null ? string.Empty : $" ({count})";
    /// <summary>Applies an eligibility filter and resets paging.</summary>
    [Parameter] public EventCallback<string> OnEligibilityChanged { get; set; }
    /// <summary>The independent team-choice search.</summary>
    [Parameter] public string? TeamSearch { get; set; }
    /// <summary>Whether the bounded team choices require a narrower search.</summary>
    [Parameter] public bool TeamChoicesTruncated { get; set; }
    /// <summary>Searches bounded active and archived team choices.</summary>
    [Parameter] public EventCallback<string> OnTeamSearchChanged { get; set; }
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
    /// Gets the selected team identifier as a select-binding string.
    /// </summary>
    private string TeamText => TeamId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Forwards a search input change to the parent page.
    /// </summary>
    /// <param name="search">The bound search text.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnSearchInputAsync(string? search)
        => OnSearchTextChanged.InvokeAsync(search ?? string.Empty);

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
    /// Builds a safe inline swatch style for a tag color.
    /// </summary>
    /// <param name="color">The tag color token.</param>
    /// <returns>The sanitized background-color style.</returns>
    private static string BuildSwatchStyle(string color)
        => $"background-color: {PlayerTagStyle.NormalizeColor(color)};";
}
