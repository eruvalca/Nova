using System.ComponentModel.DataAnnotations;
using Nova.Shared.Enums;
using Nova.Shared.Validation;

namespace Nova.Shared.Campaigns;

/// <summary>
/// Describes an administrator request to update a campaign participant's placement.
/// </summary>
public sealed record UpdateCampaignPlacementInput
{
    /// <summary>
    /// Initializes a placement update request.
    /// </summary>
    /// <param name="playerCampaignAssignmentId">The campaign participation identifier to update.</param>
    /// <param name="outcome">The new placement outcome.</param>
    /// <param name="teamId">The assigned team identifier, required only for an assigned outcome.</param>
    /// <param name="expectedConcurrencyToken">The token observed when the placement was loaded.</param>
    public UpdateCampaignPlacementInput(
        long playerCampaignAssignmentId,
        PlacementOutcome outcome,
        long? teamId,
        Guid expectedConcurrencyToken)
    {
        PlayerCampaignAssignmentId = playerCampaignAssignmentId;
        Outcome = outcome;
        TeamId = teamId;
        ExpectedConcurrencyToken = expectedConcurrencyToken;
    }

    /// <summary>
    /// Gets the campaign participation identifier to update.
    /// </summary>
    [Range(1, long.MaxValue, ErrorMessage = "A campaign participation identifier is required.")]
    public long PlayerCampaignAssignmentId { get; init; }

    /// <summary>
    /// Gets the new placement outcome.
    /// </summary>
    [EnumDataType(typeof(PlacementOutcome), ErrorMessage = "The placement outcome is invalid.")]
    public PlacementOutcome Outcome { get; init; }

    /// <summary>
    /// Gets the assigned team identifier, required only for an assigned outcome.
    /// </summary>
    [CampaignPlacementTeam]
    public long? TeamId { get; init; }

    /// <summary>
    /// Gets the token observed when the placement was loaded.
    /// </summary>
    [NotEmptyGuid(ErrorMessage = "A concurrency token is required.")]
    public Guid ExpectedConcurrencyToken { get; init; }
}

/// <summary>
/// Validates the team identifier against the placement outcome on the containing input.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class CampaignPlacementTeamAttribute : ValidationAttribute
{
    /// <summary>
    /// Validates team presence, absence, and positivity for the requested outcome.
    /// </summary>
    /// <param name="value">The optional team identifier.</param>
    /// <param name="validationContext">The containing placement-input validation context.</param>
    /// <returns>Success when the team shape is valid; otherwise the existing placement validation error.</returns>
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var input = (UpdateCampaignPlacementInput)validationContext.ObjectInstance;
        var teamId = value as long?;

        if (input.Outcome == PlacementOutcome.Assigned)
        {
            if (!teamId.HasValue)
            {
                return InvalidTeam("A team is required for an assigned outcome.", validationContext);
            }

            return teamId.Value > 0
                ? ValidationResult.Success
                : InvalidTeam("A team identifier must be greater than zero.", validationContext);
        }

        return teamId.HasValue
            ? InvalidTeam("A team is only allowed for an assigned outcome.", validationContext)
            : ValidationResult.Success;
    }

    /// <summary>
    /// Creates a validation result associated with the annotated team property.
    /// </summary>
    /// <param name="message">The placement-team validation message.</param>
    /// <param name="validationContext">The current property validation context.</param>
    /// <returns>A team-field validation result.</returns>
    private static ValidationResult InvalidTeam(string message, ValidationContext validationContext)
        => new(message, [validationContext.MemberName!]);
}
