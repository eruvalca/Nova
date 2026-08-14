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

    [Theory]
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
