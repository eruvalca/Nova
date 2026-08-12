using System.ComponentModel.DataAnnotations;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Tags;

/// <summary>
/// Creates a new club-scoped tag definition.
/// </summary>
public sealed record CreateTagDefinitionInput
{
    /// <summary>
    /// Gets the tag definition name.
    /// </summary>
    [Required]
    [NotWhitespace]
    [StringLength(80, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tag color in #RRGGBB format.
    /// </summary>
    [Required]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color must be a hex value in the format #RRGGBB.")]
    public string Color { get; init; } = "#FFFFFF";
}

/// <summary>
/// Updates an existing club-scoped tag definition.
/// </summary>
public sealed record UpdateTagDefinitionInput
{
    /// <summary>
    /// Gets the tag-definition identifier.
    /// </summary>
    [Range(1, long.MaxValue)]
    public long TagDefinitionId { get; init; }

    /// <summary>
    /// Gets the tag definition name.
    /// </summary>
    [Required]
    [NotWhitespace]
    [StringLength(80, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tag color in #RRGGBB format.
    /// </summary>
    [Required]
    [RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color must be a hex value in the format #RRGGBB.")]
    public string Color { get; init; } = "#FFFFFF";
}

/// <summary>
/// Lists tag definitions for the current club.
/// </summary>
public sealed record GetTagDefinitionsInput
{
    /// <summary>
    /// Gets a value indicating whether archived tag definitions should be included.
    /// </summary>
    public bool IncludeArchived { get; init; }

    /// <summary>
    /// Gets the maximum number of results to return.
    /// </summary>
    [Range(1, 100)]
    public int? Limit { get; init; } = 50;
}

/// <summary>
/// A tag-definition summary item used by list endpoints and the WASM client.
/// </summary>
public sealed record TagDefinitionSummary
{
    /// <summary>
    /// Gets the tag-definition identifier.
    /// </summary>
    public long TagDefinitionId { get; init; }

    /// <summary>
    /// Gets the tag name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tag color.
    /// </summary>
    public string Color { get; init; } = string.Empty;

    /// <summary>
    /// Gets the lifecycle status.
    /// </summary>
    public LifecycleStatus LifecycleStatus { get; init; }

    /// <summary>
    /// Gets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the archive timestamp when archived.
    /// </summary>
    public DateTimeOffset? ArchivedAt { get; init; }
}

/// <summary>
/// Successful tag-definition mutation payload.
/// </summary>
public sealed record TagDefinitionMutationSuccess
{
    /// <summary>
    /// Gets the tag-definition identifier.
    /// </summary>
    public long TagDefinitionId { get; init; }
}

/// <summary>
/// Server-side contract for club-scoped tag-definition management. Admins can create/update/archive,
/// and evaluators and club members can read active definitions.
/// </summary>
public interface ITagDefinitionService
{
    Task<ServiceResult<TagDefinitionMutationSuccess>> CreateAsync(CreateTagDefinitionInput input, CancellationToken cancellationToken = default);

    Task<ServiceResult<TagDefinitionMutationSuccess>> UpdateAsync(UpdateTagDefinitionInput input, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<TagDefinitionSummary>>> GetActiveAsync(GetTagDefinitionsInput? input = null, CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<TagDefinitionSummary>>> GetArchivedAsync(GetTagDefinitionsInput? input = null, CancellationToken cancellationToken = default);

    Task<ServiceResult<TagDefinitionMutationSuccess>> ArchiveAsync(long tagDefinitionId, CancellationToken cancellationToken = default);

    Task<ServiceResult<TagDefinitionMutationSuccess>> RestoreAsync(long tagDefinitionId, CancellationToken cancellationToken = default);
}
