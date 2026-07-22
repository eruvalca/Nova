using Nova.Shared.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests placement input rules without authorization or database access.
/// </summary>
public sealed class UpdateCampaignPlacementInputValidationTests
{
    /// <summary>
    /// Verifies every invalid outcome/team combination is rejected as a team-field error.
    /// </summary>
    /// <param name="outcome">The requested placement outcome.</param>
    /// <param name="teamId">The optional requested team.</param>
    [Theory]
    [InlineData(PlacementOutcome.Assigned, null)]
    [InlineData(PlacementOutcome.Undecided, 10L)]
    [InlineData(PlacementOutcome.NotSelected, 10L)]
    [InlineData(PlacementOutcome.Withdrawn, 10L)]
    public void Validate_ReturnsTeamError_ForInvalidOutcomeTeamMatrix(
        PlacementOutcome outcome,
        long? teamId)
    {
        var errors = InputValidator.Validate(
            new UpdateCampaignPlacementInput(1, outcome, teamId, Guid.NewGuid()));

        errors.ShouldContainKey(nameof(UpdateCampaignPlacementInput.TeamId));
    }

    /// <summary>
    /// Verifies every valid outcome/team combination passes model validation.
    /// </summary>
    /// <param name="outcome">The requested placement outcome.</param>
    /// <param name="teamId">The optional requested team.</param>
    [Theory]
    [InlineData(PlacementOutcome.Assigned, 10L)]
    [InlineData(PlacementOutcome.Undecided, null)]
    [InlineData(PlacementOutcome.NotSelected, null)]
    [InlineData(PlacementOutcome.Withdrawn, null)]
    public void Validate_ReturnsNoErrors_ForValidOutcomeTeamMatrix(
        PlacementOutcome outcome,
        long? teamId)
    {
        var errors = InputValidator.Validate(
            new UpdateCampaignPlacementInput(1, outcome, teamId, Guid.NewGuid()));

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies invalid scalar values remain represented by their existing field keys.
    /// </summary>
    [Fact]
    public void Validate_ReturnsAllScalarErrors_ForInvalidValues()
    {
        var errors = InputValidator.Validate(
            new UpdateCampaignPlacementInput(
                0,
                (PlacementOutcome)99,
                -1,
                Guid.Empty));

        errors.Keys.ShouldBe(
        [
            nameof(UpdateCampaignPlacementInput.PlayerCampaignAssignmentId),
            nameof(UpdateCampaignPlacementInput.Outcome),
            nameof(UpdateCampaignPlacementInput.TeamId),
            nameof(UpdateCampaignPlacementInput.ExpectedConcurrencyToken)
        ], ignoreOrder: true);
    }
}
