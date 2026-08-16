using Nova.Shared.Features.Campaigns;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests placement query input rules without authorization or database access.
/// </summary>
public sealed class GetCampaignPlacementInputValidationTests
{
    /// <summary>
    /// Verifies non-positive campaign identifiers are rejected on the roster input.
    /// </summary>
    /// <param name="campaignId">The invalid campaign identifier.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ReturnsCampaignIdError_ForNonPositiveRosterCampaignId(long campaignId)
    {
        var errors = InputValidator.Validate(
            new GetCampaignPlacementRosterInput { CampaignId = campaignId });

        errors.ShouldContainKey(nameof(GetCampaignPlacementRosterInput.CampaignId));
    }

    /// <summary>
    /// Verifies invalid paging and graduation-year values are represented by their field keys.
    /// </summary>
    [Fact]
    public void Validate_ReturnsAllScalarErrors_ForInvalidRosterValues()
    {
        var errors = InputValidator.Validate(
            new GetCampaignPlacementRosterInput
            {
                CampaignId = 1,
                GraduationYear = 0,
                Page = 0,
                PageSize = GetCampaignPlacementRosterInput.MaxPageSize + 1
            });

        errors.Keys.ShouldBe(
        [
            nameof(GetCampaignPlacementRosterInput.GraduationYear),
            nameof(GetCampaignPlacementRosterInput.Page),
            nameof(GetCampaignPlacementRosterInput.PageSize)
        ], ignoreOrder: true);
    }

    /// <summary>
    /// Verifies omitted optional filters and default paging values are valid.
    /// </summary>
    [Fact]
    public void Validate_ReturnsNoErrors_ForMinimalRosterInput()
    {
        var errors = InputValidator.Validate(
            new GetCampaignPlacementRosterInput { CampaignId = 42 });

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies explicitly supplied valid filters are accepted.
    /// </summary>
    [Fact]
    public void Validate_ReturnsNoErrors_ForPopulatedRosterInput()
    {
        var errors = InputValidator.Validate(
            new GetCampaignPlacementRosterInput
            {
                CampaignId = 42,
                GraduationYear = 2028,
                UnresolvedOnly = true,
                Page = 2,
                PageSize = 25
            });

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies page offsets that would overflow an integer are rejected by shared validation.
    /// </summary>
    [Fact]
    public void Validate_ReturnsPageError_WhenPageOffsetWouldOverflow()
    {
        var errors = InputValidator.Validate(
            new GetCampaignPlacementRosterInput
            {
                CampaignId = 42,
                Page = int.MaxValue,
                PageSize = 2
            });

        errors.Keys.ShouldBe([nameof(GetCampaignPlacementRosterInput.Page)]);
    }

    /// <summary>
    /// Verifies non-positive campaign identifiers are rejected on the summary input.
    /// </summary>
    /// <param name="campaignId">The invalid campaign identifier.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ReturnsCampaignIdError_ForNonPositiveSummaryCampaignId(long campaignId)
    {
        var errors = InputValidator.Validate(
            new GetCampaignPlacementSummaryInput { CampaignId = campaignId });

        errors.ShouldContainKey(nameof(GetCampaignPlacementSummaryInput.CampaignId));
    }

    /// <summary>
    /// Verifies a valid summary input passes model validation.
    /// </summary>
    [Fact]
    public void Validate_ReturnsNoErrors_ForValidSummaryInput()
    {
        var errors = InputValidator.Validate(
            new GetCampaignPlacementSummaryInput { CampaignId = 42 });

        errors.ShouldBeEmpty();
    }
}
