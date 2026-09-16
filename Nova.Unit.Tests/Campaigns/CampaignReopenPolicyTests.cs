using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class CampaignReopenPolicyTests
{
    [Theory]
    [InlineData(CampaignStatus.Active, 1L, 1L, 1L, false, CampaignReopenUnavailableReason.NotClosed)]
    [InlineData(CampaignStatus.Closed, 2L, 1L, 1L, false, CampaignReopenUnavailableReason.HistoricalSeason)]
    [InlineData(CampaignStatus.Closed, 1L, null, 1L, false, CampaignReopenUnavailableReason.MissingOpening)]
    [InlineData(CampaignStatus.Closed, 1L, null, 1L, true, CampaignReopenUnavailableReason.MissingOpening)]
    [InlineData(CampaignStatus.Closed, 1L, 1L, 2L, false, CampaignReopenUnavailableReason.LaterCampaignOpened)]
    [InlineData(CampaignStatus.Closed, 1L, 1L, 1L, true, CampaignReopenUnavailableReason.AnotherActiveCampaign)]
    public void ReopenRestrictionsUseSeasonIdentityAndAuthoritativeOpeningOrder(CampaignStatus status, long currentSeason, long? opening, long latest, bool anotherActive, CampaignReopenUnavailableReason reason)
    {
        CampaignReopenPolicy.Evaluate(status, 1, currentSeason, opening, latest, anotherActive)
            .Value.ShouldBeOfType<CampaignReopenBlocked>().Reason.ShouldBe(reason);
    }

    [Fact]
    public void LatestClosedCampaignCanReopenWithoutDiscardingItsOutcomes()
        => CampaignReopenPolicy.Evaluate(CampaignStatus.Closed, 1, 1, 7, 7, false).Value.ShouldBeOfType<CampaignMayReopen>();
}
