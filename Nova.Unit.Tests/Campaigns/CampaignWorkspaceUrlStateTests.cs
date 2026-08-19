using Nova.UI.Features.Campaigns.Services;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Pure unit tests for the campaign workspace roster URL-state round-tripping: canonical query
/// building, defensive parsing, token normalization, filter detection, and page-count math.
/// </summary>
public sealed class CampaignWorkspaceUrlStateTests
{
    // ── Round-trip ─────────────────────────────────────────────────────────────

    [Fact]
    public void BuildQueryString_ThenParse_RoundTripsEveryField()
    {
        var state = new CampaignWorkspaceRosterState
        {
            Search = "avery",
            GraduationYears = [2032, 2031],
            TagDefinitionIds = [12, 11],
            Outcome = "assigned",
            TeamId = 21,
            SortBy = "displayName",
            SortDirection = "desc",
            Page = 3
        };

        var query = CampaignWorkspaceUrlState.BuildQueryString(state);
        query.ShouldBe("search=avery&graduationYears=2031,2032&tagIds=11,12&outcome=assigned&teamId=21&sortBy=displayName&sortDirection=desc&page=3");

        var parsed = ParseFromQuery(query);
        parsed.Search.ShouldBe("avery");
        parsed.GraduationYears.ShouldBe([2031, 2032]);
        parsed.TagDefinitionIds.ShouldBe([11, 12]);
        parsed.Outcome.ShouldBe("assigned");
        parsed.TeamId.ShouldBe(21L);
        parsed.SortBy.ShouldBe("displayName");
        parsed.SortDirection.ShouldBe("desc");
        parsed.Page.ShouldBe(3);
    }

    [Fact]
    public void BuildQueryString_OmitsDefaults()
    {
        CampaignWorkspaceUrlState.BuildQueryString(new CampaignWorkspaceRosterState()).ShouldBeEmpty();
        CampaignWorkspaceUrlState.BuildQueryString(new CampaignWorkspaceRosterState { Page = 1 }).ShouldBeEmpty();
        CampaignWorkspaceUrlState.BuildQueryString(new CampaignWorkspaceRosterState { SortBy = "displayName" }).ShouldBeEmpty();
    }

    // ── Defensive parsing ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_FallsBackToDefaults_ForInvalidValues()
    {
        var parsed = CampaignWorkspaceUrlState.Parse(
            search: "   ",
            graduationYears: "2032,abc,-5,0",
            tagDefinitionIds: "x,12,12,-1",
            outcome: "garbage",
            teamId: 0,
            sortBy: "assignmentId",
            sortDirection: "sideways",
            page: -3);

        parsed.Search.ShouldBeNull();
        parsed.GraduationYears.ShouldBe([2032]);
        parsed.TagDefinitionIds.ShouldBe([12]);
        parsed.Outcome.ShouldBeNull();
        parsed.TeamId.ShouldBeNull();
        parsed.SortBy.ShouldBeNull();
        parsed.SortDirection.ShouldBeNull();
        parsed.Page.ShouldBe(1);
    }

    [Fact]
    public void Parse_KeepsFirstSeenOrder_AndDeduplicatesLists()
    {
        var parsed = CampaignWorkspaceUrlState.Parse(
            search: null,
            graduationYears: "2032,2031,2032,2033,2031",
            tagDefinitionIds: "12,11,12,13,11",
            outcome: null,
            teamId: null,
            sortBy: null,
            sortDirection: null,
            page: null);

        parsed.GraduationYears.ShouldBe([2032, 2031, 2033]);
        parsed.TagDefinitionIds.ShouldBe([12, 11, 13]);
    }

    [Fact]
    public void Parse_NormalizesTokens_CaseInsensitively()
    {
        var parsed = CampaignWorkspaceUrlState.Parse(null, null, null, "ASSIGNED", null, "DisplayName", "DESC", 1);

        parsed.Outcome.ShouldBe("assigned");
        parsed.SortBy.ShouldBe("displayName");
        parsed.SortDirection.ShouldBe("desc");
    }

    // ── Page math ──────────────────────────────────────────────────────────────

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0, 50, 1)]
    [InlineData(1, 50, 1)]
    [InlineData(50, 50, 1)]
    [InlineData(51, 50, 2)]
    [InlineData(120, 50, 3)]
    [InlineData(12, 0, 1)]
    public void CalculatePageCount_ReturnsExpectedPages(int totalCount, int pageSize, int expected)
    {
        CampaignWorkspaceUrlState.CalculatePageCount(totalCount, pageSize).ShouldBe(expected);
    }

    // ── Workspace URL ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildWorkspaceUrl_AlwaysIncludesTab_AndOmitsDefaults()
    {
        CampaignWorkspaceUrlState.BuildWorkspaceUrl(10, new CampaignWorkspaceRosterState())
            .ShouldBe("/campaigns/10?tab=evaluate");

        CampaignWorkspaceUrlState.BuildWorkspaceUrl(10, new CampaignWorkspaceRosterState { Search = "ave" })
            .ShouldBe("/campaigns/10?search=ave&tab=evaluate");

        CampaignWorkspaceUrlState.BuildWorkspaceUrl(
                10,
                new CampaignWorkspaceRosterState { Page = 2 },
                tab: "evaluate")
            .ShouldBe("/campaigns/10?page=2&tab=evaluate");
    }

    // ── Filter detection ───────────────────────────────────────────────────────

    [Fact]
    public void HasActiveFilters_AndClearFilters_TrackFilterPresence()
    {
        var state = new CampaignWorkspaceRosterState { Search = "ave", TeamId = 21, Page = 4 };
        CampaignWorkspaceUrlState.HasActiveFilters(state).ShouldBeTrue();

        var cleared = CampaignWorkspaceUrlState.ClearFilters(state);
        cleared.Search.ShouldBeNull();
        cleared.TeamId.ShouldBeNull();
        cleared.Page.ShouldBe(1);
        CampaignWorkspaceUrlState.HasActiveFilters(cleared).ShouldBeFalse();
    }

    // ── Participant selection ──────────────────────────────────────────────────

    [Fact]
    public void ParseParticipant_ReturnsNull_ForMissingOrInvalidValues()
    {
        CampaignWorkspaceUrlState.ParseParticipant(null).ShouldBeNull();
        CampaignWorkspaceUrlState.ParseParticipant("").ShouldBeNull();
        CampaignWorkspaceUrlState.ParseParticipant("abc").ShouldBeNull();
        CampaignWorkspaceUrlState.ParseParticipant("0").ShouldBeNull();
        CampaignWorkspaceUrlState.ParseParticipant("-5").ShouldBeNull();
        CampaignWorkspaceUrlState.ParseParticipant("12.5").ShouldBeNull();
    }

    [Fact]
    public void ParseParticipant_ReturnsPositiveLong_ForValidValues()
    {
        CampaignWorkspaceUrlState.ParseParticipant("301").ShouldBe(301L);
    }

    [Fact]
    public void BuildWorkspaceUrl_AppendsParticipantAfterTab_WhenOpen()
    {
        CampaignWorkspaceUrlState.BuildWorkspaceUrl(
                10,
                new CampaignWorkspaceRosterState { Search = "ave" },
                participantId: 301)
            .ShouldBe("/campaigns/10?search=ave&tab=evaluate&participant=301");
    }

    [Fact]
    public void BuildWorkspaceUrl_OmitsParticipant_WhenClosed()
    {
        CampaignWorkspaceUrlState.BuildWorkspaceUrl(10, new CampaignWorkspaceRosterState())
            .ShouldBe("/campaigns/10?tab=evaluate");
    }

    // ── Tab normalization ─────────────────────────────────────────────────────

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, "evaluate")]
    [InlineData("evaluate", "evaluate")]
    [InlineData("EVALUATE", "evaluate")]
    [InlineData("placements", "placements")]
    [InlineData("PLACEMENTS", "placements")]
    [InlineData("overview", "overview")]
    [InlineData("OVERVIEW", "overview")]
    [InlineData("closeout", "closeout")]
    [InlineData("CLOSEOUT", "closeout")]
    [InlineData("garbage", "evaluate")]
    public void NormalizeTab_ReturnsCanonicalToken_OrEvaluateFallback(string? raw, string expected)
    {
        CampaignWorkspaceUrlState.NormalizeTab(raw).ShouldBe(expected);
    }

    // ── Placement state ───────────────────────────────────────────────────────

    [Fact]
    public void ParsePlacement_ThenBuild_RoundTripsEveryField()
    {
        var state = CampaignWorkspaceUrlState.ParsePlacement(2032, true, 3);
        state.GraduationYear.ShouldBe(2032);
        state.UnresolvedOnly.ShouldBeTrue();
        state.Page.ShouldBe(3);

        CampaignWorkspaceUrlState.BuildPlacementQueryString(state)
            .ShouldBe("placementGraduationYear=2032&unresolvedOnly=true&placementPage=3");
    }

    [Fact]
    public void ParsePlacement_FallsBackToDefaults_ForInvalidValues()
    {
        var state = CampaignWorkspaceUrlState.ParsePlacement(0, null, -3);
        state.GraduationYear.ShouldBeNull();
        state.UnresolvedOnly.ShouldBeFalse();
        state.Page.ShouldBe(1);
    }

    [Fact]
    public void BuildPlacementQueryString_OmitsDefaults()
    {
        CampaignWorkspaceUrlState.BuildPlacementQueryString(new CampaignWorkspacePlacementState()).ShouldBeEmpty();
        CampaignWorkspaceUrlState.BuildPlacementQueryString(new CampaignWorkspacePlacementState { Page = 1 }).ShouldBeEmpty();
        CampaignWorkspaceUrlState.BuildPlacementQueryString(new CampaignWorkspacePlacementState { UnresolvedOnly = false }).ShouldBeEmpty();
    }

    [Fact]
    public void BuildPlacementsWorkspaceUrl_IsolatesPlacementParams_FromRosterParams()
    {
        CampaignWorkspaceUrlState.BuildPlacementsWorkspaceUrl(10, new CampaignWorkspacePlacementState())
            .ShouldBe("/campaigns/10?tab=placements");

        CampaignWorkspaceUrlState.BuildPlacementsWorkspaceUrl(
                10,
                new CampaignWorkspacePlacementState { GraduationYear = 2032, UnresolvedOnly = true, Page = 2 })
            .ShouldBe("/campaigns/10?placementGraduationYear=2032&unresolvedOnly=true&placementPage=2&tab=placements");
    }

    [Fact]
    public void BuildWorkspaceUrl_DoesNotEmitPlacementParams_ForRosterState()
    {
        CampaignWorkspaceUrlState.BuildWorkspaceUrl(10, new CampaignWorkspaceRosterState { Search = "ave" })
            .ShouldBe("/campaigns/10?search=ave&tab=evaluate");
    }

    // ── Overview / closeout / review-unresolved URLs ──────────────────────────

    [Fact]
    public void BuildOverviewWorkspaceUrl_EmitsOnlyOverviewTab()
    {
        CampaignWorkspaceUrlState.BuildOverviewWorkspaceUrl(10)
            .ShouldBe("/campaigns/10?tab=overview");
    }

    [Fact]
    public void BuildCloseoutWorkspaceUrl_EmitsOnlyCloseoutTab()
    {
        CampaignWorkspaceUrlState.BuildCloseoutWorkspaceUrl(10)
            .ShouldBe("/campaigns/10?tab=closeout");
    }

    [Fact]
    public void BuildReviewUnresolvedUrl_EmitsUnresolvedOnly_AndPlacementsTab()
    {
        CampaignWorkspaceUrlState.BuildReviewUnresolvedUrl(10)
            .ShouldBe("/campaigns/10?unresolvedOnly=true&tab=placements");
    }

    [Fact]
    public void OverviewAndCloseoutTabTokens_AreCanonicalAndNormalizeRoundTrip()
    {
        CampaignWorkspaceUrlState.NormalizeTab(CampaignWorkspaceUrlState.OverviewTab).ShouldBe("overview");
        CampaignWorkspaceUrlState.NormalizeTab(CampaignWorkspaceUrlState.CloseoutTab).ShouldBe("closeout");
        CampaignWorkspaceUrlState.NormalizeTab("OVERVIEW").ShouldBe("overview");
        CampaignWorkspaceUrlState.NormalizeTab("CLOSEOUT").ShouldBe("closeout");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static CampaignWorkspaceRosterState ParseFromQuery(string query)
    {
        string? ValueOf(string key)
        {
            foreach (var pair in query.Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (parts[0] == key)
                {
                    return Uri.UnescapeDataString(parts[1]);
                }
            }

            return null;
        }

        int? IntOf(string key) => int.TryParse(ValueOf(key), out var value) ? value : null;
        long? LongOf(string key) => long.TryParse(ValueOf(key), out var value) ? value : null;

        return CampaignWorkspaceUrlState.Parse(
            ValueOf("search"),
            ValueOf("graduationYears"),
            ValueOf("tagIds"),
            ValueOf("outcome"),
            LongOf("teamId"),
            ValueOf("sortBy"),
            ValueOf("sortDirection"),
            IntOf("page"));
    }
}
