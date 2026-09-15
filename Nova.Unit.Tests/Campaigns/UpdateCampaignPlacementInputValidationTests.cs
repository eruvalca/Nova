using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests placement input rules without authorization or database access.
/// </summary>
public sealed class UpdateCampaignPlacementInputValidationTests
{
    [Fact]
    public void ValidateRejectsMissingOperationIdentity()
    {
        var errors = InputValidator.Validate(new UpdateCampaignPlacementInput(1, PlacementOutcome.NotSelected,
            null, Guid.NewGuid(), Guid.Empty));

        errors.ShouldContainKey(nameof(UpdateCampaignPlacementInput.OperationId));
        errors.Count.ShouldBe(1);
    }
    /// <summary>
    /// Verifies every invalid outcome/team combination is rejected as a team-field error.
    /// </summary>
    /// <param name="outcome">The requested placement outcome.</param>
    /// <param name="teamId">The optional requested team.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Assigned, null)]
    [InlineData(PlacementOutcome.Undecided, 10L)]
    [InlineData(PlacementOutcome.NotSelected, 10L)]
    [InlineData(PlacementOutcome.Withdrawn, 10L)]
    public void ValidateReturnsTeamErrorForInvalidOutcomeTeamMatrix(
        PlacementOutcome outcome,
        long? teamId)
    {
        var errors = InputValidator.Validate(
            new UpdateCampaignPlacementInput(1, outcome, teamId, Guid.NewGuid(), operationId: Guid.CreateVersion7()));

        errors.ShouldContainKey(nameof(UpdateCampaignPlacementInput.TeamId));
    }

    /// <summary>
    /// Verifies every valid outcome/team combination passes model validation.
    /// </summary>
    /// <param name="outcome">The requested placement outcome.</param>
    /// <param name="teamId">The optional requested team.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Assigned, 10L)]
    [InlineData(PlacementOutcome.NotSelected, null)]
    [InlineData(PlacementOutcome.Withdrawn, null)]
    public void ValidateReturnsNoErrorsForValidOutcomeTeamMatrix(
        PlacementOutcome outcome,
        long? teamId)
    {
        var errors = InputValidator.Validate(
            new UpdateCampaignPlacementInput(1, outcome, teamId, Guid.NewGuid(), operationId: Guid.CreateVersion7()));

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies technical enrollment cannot be saved as a decision or used to clear history.
    /// </summary>
    [Fact]
    public void ValidateReturnsOutcomeErrorForUndecidedWithoutTeam()
    {
        var errors = InputValidator.Validate(
            new UpdateCampaignPlacementInput(1, PlacementOutcome.Undecided, null, Guid.NewGuid(), operationId: Guid.CreateVersion7()));

        errors.ShouldContainKey(nameof(UpdateCampaignPlacementInput.Outcome));
    }

    /// <summary>
    /// Verifies invalid scalar values remain represented by their existing field keys.
    /// </summary>
    [Fact]
    public void ValidateReturnsAllScalarErrorsForInvalidValues()
    {
        var errors = InputValidator.Validate(
            new UpdateCampaignPlacementInput(
                0,
                (PlacementOutcome)99,
                -1,
                Guid.Empty, operationId: Guid.CreateVersion7()));

        errors.Keys.ShouldBe(
        [
            nameof(UpdateCampaignPlacementInput.PlayerCampaignAssignmentId),
            nameof(UpdateCampaignPlacementInput.Outcome),
            nameof(UpdateCampaignPlacementInput.TeamId),
            nameof(UpdateCampaignPlacementInput.ExpectedConcurrencyToken)
        ], ignoreOrder: true);
    }
}
