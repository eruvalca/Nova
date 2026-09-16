using Nova.UI.Features.Campaigns.Services;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class CampaignWorkspaceCloseStateTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, 1)]
    [InlineData(int.MinValue, 1)]
    [InlineData(0, 1)]
    [InlineData(2, 2)]
    [InlineData(42_949_673, 42_949_673)]
    [InlineData(42_949_674, 1)]
    [InlineData(int.MaxValue, 1)]
    public void ClosePageUrlsRespectTheDiscoveryOffsetBoundary(int? raw, int expected)
    {
        CampaignWorkspaceCloseState.NormalizePage(raw).ShouldBe(expected);
        new CampaignWorkspaceCloseState { Page = raw ?? 1 }.Apply("/campaigns/10?tab=close")
            .ShouldBe(expected == 1 ? "/campaigns/10?tab=close" : $"/campaigns/10?tab=close&closePage={expected}");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0, false)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public void CloseSearchUrlsTrimAndEnforceTheSharedBoundary(int length, bool accepted)
    {
        var raw = "  " + new string('a', length) + "  ";
        var normalized = CampaignWorkspaceCloseState.NormalizeSearch(raw);
        normalized.ShouldBe(accepted ? raw.Trim() : null);
        new CampaignWorkspaceCloseState { Search = raw }.Apply("/campaigns/10?tab=close")
            .ShouldBe(accepted ? "/campaigns/10?tab=close&closeSearch=" + raw.Trim() : "/campaigns/10?tab=close");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("unknown", null)]
    [InlineData(" OUTCOMES ", "outcomes")]
    [InlineData("ELIGIBILITY", "eligibility")]
    [InlineData("ARCHIVEDTEAMS", "archivedTeams")]
    public void CloseBlockerUrlsUseCanonicalKeysOrOmitUnknownValues(string? raw, string? expected)
    {
        CampaignWorkspaceCloseState.NormalizeBlocker(raw).ShouldBe(expected);
        var state = new CampaignWorkspaceCloseState { Blocker = raw };
        state.Apply("/campaigns/10?tab=close").ShouldBe(expected is null
            ? "/campaigns/10?tab=close" : $"/campaigns/10?tab=close&closeBlocker={expected}");
    }
}
