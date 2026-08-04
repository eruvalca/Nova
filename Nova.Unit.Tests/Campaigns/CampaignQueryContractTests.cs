using Nova.Shared.Campaigns;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class CampaignQueryContractTests
{
    [Fact]
    public void GetCampaignListUrl_BuildsExpectedUrl()
    {
        var url = CampaignEndpoints.GetCampaignListUrl(" Active ", 25);

        url.ShouldBe("/api/campaigns?status=active&limit=25");
    }

    [Fact]
    public void GetCampaignListInput_DefaultsToNoValidationErrors_WhenOmitted()
    {
        var errors = InputValidator.Validate(new GetCampaignListInput());
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void GetCampaignListInput_RejectsInvalidStatus()
    {
        var errors = InputValidator.Validate(new GetCampaignListInput { Status = "open" });
        errors.ShouldContainKey(nameof(GetCampaignListInput.Status));
    }

    [Fact]
    public void GetCampaignListUrl_OmitsInvalidOptionalValues()
    {
        var url = CampaignEndpoints.GetCampaignListUrl(" ", 0);
        url.ShouldBe("/api/campaigns");
    }
}
