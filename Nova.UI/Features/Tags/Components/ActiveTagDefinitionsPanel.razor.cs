using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Tags;
using Nova.UI.Components;
using Nova.UI.Features.Players;

namespace Nova.UI.Features.Tags.Components;

/// <summary>
/// Read-only panel that lists the club's active tag definitions as badges.
/// Safe for any club member or evaluator; load failures degrade to an empty state.
/// </summary>
public partial class ActiveTagDefinitionsPanel(ITagDefinitionService tagDefinitionService) : NovaComponentBase
{
    private bool _loading = true;

    /// <summary>
    /// The active tag definitions for the current club.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<TagDefinitionSummary>? Active { get; set; }

    /// <summary>
    /// Whether the active list has already been loaded during prerendering.
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

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        if (Initialized)
        {
            _loading = false;
            return;
        }

        var result = await tagDefinitionService.GetActiveAsync(null, ComponentCancellationToken);
        result.Switch(
            active => Active = active,
            _ => Active = []);
        Initialized = true;
        _loading = false;
    }
}
