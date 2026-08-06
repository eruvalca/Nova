using Nova.Shared.Features.Campaigns;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies shared campaign query validation and route contracts.
/// </summary>
public sealed class CampaignQueryContractTests
{
    /// <summary>Verifies accepted filters produce the expected normalized URL.</summary>
    [Fact]
    public void GetCampaignListUrl_BuildsExpectedUrl()
    {
        var url = CampaignEndpoints.GetCampaignListUrl(" Active ", 25);

        url.ShouldBe("/api/campaigns?status=active&limit=25");
    }

    /// <summary>Verifies omitted filters satisfy the shared input contract.</summary>
    [Fact]
    public void GetCampaignListInput_DefaultsToNoValidationErrors_WhenOmitted()
    {
        var errors = InputValidator.Validate(new GetCampaignListInput());
        errors.ShouldBeEmpty();
    }

    /// <summary>Verifies unsupported status values are rejected.</summary>
    [Fact]
    public void GetCampaignListInput_RejectsInvalidStatus()
    {
        var errors = InputValidator.Validate(new GetCampaignListInput { Status = "open" });
        errors.ShouldContainKey(nameof(GetCampaignListInput.Status));
    }

    /// <summary>Verifies an explicitly empty status is rejected.</summary>
    [Fact]
    public void GetCampaignListInput_RejectsEmptyStatus()
    {
        var errors = InputValidator.Validate(new GetCampaignListInput { Status = string.Empty });
        errors.ShouldContainKey(nameof(GetCampaignListInput.Status));
    }

    /// <summary>Verifies invalid optional values are omitted by the URL builder.</summary>
    [Fact]
    public void GetCampaignListUrl_OmitsInvalidOptionalValues()
    {
        var url = CampaignEndpoints.GetCampaignListUrl(" ", 0);
        url.ShouldBe("/api/campaigns");
    }

    /// <summary>
    /// Verifies the declared inclusive list-limit validation bounds.
    /// </summary>
    /// <param name="limit">The explicit limit to validate.</param>
    /// <param name="isValid">Whether the limit is valid.</param>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void GetCampaignListInput_ValidatesLimitBounds(int limit, bool isValid)
    {
        var errors = InputValidator.Validate(new GetCampaignListInput { Limit = limit });

        errors.ContainsKey(nameof(GetCampaignListInput.Limit)).ShouldBe(!isValid);
    }

    /// <summary>
    /// Verifies both accepted status values are case-insensitive.
    /// </summary>
    /// <param name="status">The status spelling to validate.</param>
    [Theory]
    [InlineData("active")]
    [InlineData("ACTIVE")]
    [InlineData("closed")]
    [InlineData("CLOSED")]
    public void GetCampaignListInput_AcceptsSupportedStatusCaseInsensitively(string status)
    {
        var errors = InputValidator.Validate(new GetCampaignListInput { Status = status });

        errors.ShouldBeEmpty();
    }
}
