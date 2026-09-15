using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class PlacementContextEndpointTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, "")]
    [InlineData(0L, "")]
    [InlineData(-1L, "")]
    [InlineData(1L, "?beforeEventId=1")]
    [InlineData(long.MaxValue, "?beforeEventId=9223372036854775807")]
    public void ContextUrlIncludesOnlyPositiveCursors(long? cursor, string query)
    {
        var input = new GetPlacementContextInput { CampaignId = 10, PlayerCampaignAssignmentId = 301, BeforeEventId = cursor };

        var url = PlacementContextEndpoints.Url(input);

        url.IsAbsoluteUri.ShouldBeFalse();
        url.OriginalString.ShouldBe("/api/campaigns/10/participants/301/placement-context" + query);
    }
}
