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
    public void BuildQueryStringThenParseRoundTripsEveryField()
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
    public void BuildQueryStringOmitsDefaults()
    {
        CampaignWorkspaceUrlState.BuildQueryString(new CampaignWorkspaceRosterState()).ShouldBeEmpty();
        CampaignWorkspaceUrlState.BuildQueryString(new CampaignWorkspaceRosterState { Page = 1 }).ShouldBeEmpty();
        CampaignWorkspaceUrlState.BuildQueryString(new CampaignWorkspaceRosterState { SortBy = "displayName" }).ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("graduationYear", null, "graduationYear", "asc")]
    [InlineData(null, "desc", "displayName", "desc")]
    [InlineData("teamName", null, "teamName", "asc")]
    public void BuildQueryStringPreservesPartialNondefaultSortThroughReturnNavigation(
        string? field, string? direction, string expectedField, string expectedDirection)
    {
        var state = new CampaignWorkspaceRosterState { SortBy = field, SortDirection = direction };
        var query = CampaignWorkspaceUrlState.BuildQueryString(state);
        query.ShouldBe($"sortBy={expectedField}&sortDirection={expectedDirection}");
        var restored = ParseFromQuery(query);
        restored.SortBy.ShouldBe(expectedField);
        restored.SortDirection.ShouldBe(expectedDirection);
        CampaignWorkspaceUrlState.BuildEvaluateWorkspaceUrl(10, state)
            .ShouldBe($"/campaigns/10?{query}&tab=evaluate");
    }

    // ── Defensive parsing ──────────────────────────────────────────────────────

    [Fact]
    public void ParseFallsBackToDefaultsForInvalidValues()
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
    public void ParseKeepsFirstSeenOrderAndDeduplicatesLists()
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
    public void ParseNormalizesTokensCaseInsensitively()
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
    public void CalculatePageCountReturnsExpectedPages(int totalCount, int pageSize, int expected)
    {
        CampaignWorkspaceUrlState.CalculatePageCount(totalCount, pageSize).ShouldBe(expected);
    }

    // ── Workspace URL ──────────────────────────────────────────────────────────

    [Fact]
    public void BuildWorkspaceUrlAlwaysIncludesTabAndOmitsDefaults()
    {
        CampaignWorkspaceUrlState.BuildWorkspaceUrl(10, new CampaignWorkspaceRosterState())
            .ShouldBe("/campaigns/10?tab=roster");

        CampaignWorkspaceUrlState.BuildWorkspaceUrl(10, new CampaignWorkspaceRosterState { Search = "ave" })
            .ShouldBe("/campaigns/10?search=ave&tab=roster");

        CampaignWorkspaceUrlState.BuildWorkspaceUrl(
                10,
                new CampaignWorkspaceRosterState { Page = 2 },
                tab: "evaluate")
            .ShouldBe("/campaigns/10?page=2&tab=evaluate");
    }

    // ── Filter detection ───────────────────────────────────────────────────────

    [Fact]
    public void HasActiveFiltersAndClearFiltersTrackFilterPresence()
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
    public void ParseParticipantReturnsNullForMissingOrInvalidValues()
    {
        CampaignWorkspaceUrlState.ParseParticipant(null).ShouldBeNull();
        CampaignWorkspaceUrlState.ParseParticipant("").ShouldBeNull();
        CampaignWorkspaceUrlState.ParseParticipant("abc").ShouldBeNull();
        CampaignWorkspaceUrlState.ParseParticipant("0").ShouldBeNull();
        CampaignWorkspaceUrlState.ParseParticipant("-5").ShouldBeNull();
        CampaignWorkspaceUrlState.ParseParticipant("12.5").ShouldBeNull();
    }

    [Fact]
    public void ParseParticipantReturnsPositiveLongForValidValues()
    {
        CampaignWorkspaceUrlState.ParseParticipant("301").ShouldBe(301L);
    }

    [Fact]
    public void BuildWorkspaceUrlAppendsParticipantAfterTabWhenOpen()
    {
        CampaignWorkspaceUrlState.BuildWorkspaceUrl(
                10,
                new CampaignWorkspaceRosterState { Search = "ave" },
                participantId: 301)
            .ShouldBe("/campaigns/10?search=ave&tab=roster&participant=301");
    }

    [Fact]
    public void BuildWorkspaceUrlOmitsParticipantWhenClosed()
    {
        CampaignWorkspaceUrlState.BuildWorkspaceUrl(10, new CampaignWorkspaceRosterState())
            .ShouldBe("/campaigns/10?tab=roster");
    }

    // ── Tab normalization ─────────────────────────────────────────────────────

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, "roster")]
    [InlineData("roster", "roster")]
    [InlineData("ROSTER", "roster")]
    [InlineData("evaluate", "evaluate")]
    [InlineData("EVALUATE", "evaluate")]
    [InlineData("placements", "roster")]
    [InlineData(" PLACE ", "place")]
    [InlineData("place", "place")]
    [InlineData("overview", "roster")]
    [InlineData(" EVALUATE ", "evaluate")]
    [InlineData("closeout", "roster")]
    [InlineData(" CLOSE ", "close")]
    [InlineData("close", "close")]
    [InlineData("garbage", "roster")]
    public void NormalizeTabReturnsCanonicalTokenOrRosterFallback(string? raw, string expected)
    {
        CampaignWorkspaceUrlState.NormalizeTab(raw).ShouldBe(expected);
    }

    // ── Placement state ───────────────────────────────────────────────────────

    [Fact]
    public void ParsePlacementThenBuildRoundTripsEveryField()
    {
        var state = CampaignWorkspaceUrlState.ParsePlacement(2032, true, 3);
        state.GraduationYear.ShouldBe(2032);
        state.UnresolvedOnly.ShouldBeTrue();
        state.Page.ShouldBe(3);

        CampaignWorkspaceUrlState.BuildPlacementQueryString(state)
            .ShouldBe("placementGraduationYear=2032&unresolvedOnly=true&placementPage=3");
    }

    [Fact]
    public void ParsePlacementFallsBackToDefaultsForInvalidValues()
    {
        var state = CampaignWorkspaceUrlState.ParsePlacement(0, null, -3);
        state.GraduationYear.ShouldBeNull();
        state.UnresolvedOnly.ShouldBeFalse();
        state.Page.ShouldBe(1);
    }

    [Fact]
    public void BuildPlacementQueryStringOmitsDefaults()
    {
        CampaignWorkspaceUrlState.BuildPlacementQueryString(new CampaignWorkspacePlacementState()).ShouldBeEmpty();
        CampaignWorkspaceUrlState.BuildPlacementQueryString(new CampaignWorkspacePlacementState { Page = 1 }).ShouldBeEmpty();
        CampaignWorkspaceUrlState.BuildPlacementQueryString(new CampaignWorkspacePlacementState { UnresolvedOnly = false }).ShouldBeEmpty();
    }

    [Fact]
    public void BuildPlaceWorkspaceUrlIsolatesPlacementParamsFromRosterParams()
    {
        CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(10, new CampaignWorkspacePlacementState())
            .ShouldBe("/campaigns/10?tab=place");

        CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(
                10,
                new CampaignWorkspacePlacementState { GraduationYear = 2032, UnresolvedOnly = true, Page = 2 })
            .ShouldBe("/campaigns/10?placementGraduationYear=2032&unresolvedOnly=true&placementPage=2&tab=place");
    }

    [Fact]
    public void BuildWorkspaceUrlDoesNotEmitPlacementParamsForRosterState()
    {
        CampaignWorkspaceUrlState.BuildWorkspaceUrl(10, new CampaignWorkspaceRosterState { Search = "ave" })
            .ShouldBe("/campaigns/10?search=ave&tab=roster");
    }

    // ── Overview / closeout / review-unresolved URLs ──────────────────────────

    [Fact]
    public void BuildEvaluateWorkspaceUrlEmitsOnlyEvaluateTab()
    {
        CampaignWorkspaceUrlState.BuildEvaluateWorkspaceUrl(10)
            .ShouldBe("/campaigns/10?tab=evaluate");
    }

    [Fact]
    public void BuildCloseWorkspaceUrlEmitsOnlyCloseTab()
    {
        CampaignWorkspaceUrlState.BuildCloseWorkspaceUrl(10)
            .ShouldBe("/campaigns/10?tab=close");
    }

    [Fact]
    public void BuildReviewUnresolvedUrlEmitsUnresolvedOnlyAndPlacementsTab()
    {
        CampaignWorkspaceUrlState.BuildReviewUnresolvedUrl(10)
            .ShouldBe("/campaigns/10?unresolvedOnly=true&tab=place");
    }

    [Fact]
    public void EvaluateAndCloseTabTokensAreCanonicalAndNormalizeRoundTrip()
    {
        CampaignWorkspaceUrlState.NormalizeTab(CampaignWorkspaceUrlState.EvaluateTab).ShouldBe("evaluate");
        CampaignWorkspaceUrlState.NormalizeTab(CampaignWorkspaceUrlState.CloseTab).ShouldBe("close");
        CampaignWorkspaceUrlState.NormalizeTab("EVALUATE").ShouldBe("evaluate");
        CampaignWorkspaceUrlState.NormalizeTab("CLOSE").ShouldBe("close");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("NEEDSPLACEMENT", "NeedsPlacement")]
    [InlineData("optionalreassignment", "OptionalReassignment")]
    [InlineData("RESOLVED", "Resolved")]
    [InlineData("unavailable", "Unavailable")]
    [InlineData("undecided", null)]
    public void EligibilityRoundTripsCanonicalTokensWithoutChangingLocalOutcome(string supplied, string? expected)
    {
        var parsed = CampaignWorkspaceUrlState.Parse(null, null, null, "undecided", 21, null, null, 3, supplied);
        parsed.Eligibility.ShouldBe(expected);
        parsed.Outcome.ShouldBe("undecided");
        parsed.TeamId.ShouldBe(21L);
        var roundTrip = ParseFromQuery(CampaignWorkspaceUrlState.BuildQueryString(parsed));
        roundTrip.Eligibility.ShouldBe(expected);
        roundTrip.Outcome.ShouldBe("undecided");
        roundTrip.TeamId.ShouldBe(21L);
    }

    [Fact]
    public void ClearingEligibilityResetsPageAndPreservesSorting()
    {
        var state = new CampaignWorkspaceRosterState { Eligibility = "needsPlacement", Page = 4, SortBy = "graduationYear", SortDirection = "desc" };
        CampaignWorkspaceUrlState.HasActiveFilters(state).ShouldBeTrue();
        var cleared = CampaignWorkspaceUrlState.ClearFilters(state);
        cleared.Eligibility.ShouldBeNull();
        cleared.Page.ShouldBe(1);
        cleared.SortBy.ShouldBe("graduationYear");
        cleared.SortDirection.ShouldBe("desc");
        CampaignWorkspaceUrlState.HasActiveFilters(cleared).ShouldBeFalse();
    }

    [Fact]
    public void DestinationUrlsRetainCompleteRosterReturnContext()
    {
        var state = new CampaignWorkspaceRosterState
        {
            Search = "Avery & Morgan",
            GraduationYears = [2031, 2032],
            TagDefinitionIds = [11, 12],
            Outcome = "undecided",
            TeamId = 21,
            Eligibility = "needsPlacement",
            SortBy = "tryoutNumber",
            SortDirection = "desc",
            Page = 3
        };
        var query = CampaignWorkspaceUrlState.BuildQueryString(state);
        CampaignWorkspaceUrlState.BuildEvaluateWorkspaceUrl(10, state, 301)
            .ShouldBe($"/campaigns/10?{query}&tab=evaluate&participant=301");
        CampaignWorkspaceUrlState.BuildCloseWorkspaceUrl(10, state, 301)
            .ShouldBe($"/campaigns/10?{query}&tab=close&participant=301");
        CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(10, new() { GraduationYear = 2032, Page = 2 }, state, 301)
            .ShouldBe($"/campaigns/10?{query}&participant=301&placementGraduationYear=2032&placementPage=2&tab=place");
    }

    private static CampaignWorkspaceRosterState ParseFromQuery(string query)
    {
        string? ValueOf(string key)
        {
            foreach (var pair in query.Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (string.Equals(parts[0], key, StringComparison.Ordinal))
                {
                    return Uri.UnescapeDataString(parts[1]);
                }
            }

            return null;
        }

        int? IntOf(string key) => int.TryParse(ValueOf(key), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
        long? LongOf(string key) => long.TryParse(ValueOf(key), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

        return CampaignWorkspaceUrlState.Parse(
            ValueOf("search"),
            ValueOf("graduationYears"),
            ValueOf("tagIds"),
            ValueOf("outcome"),
            LongOf("teamId"),
            ValueOf("sortBy"),
            ValueOf("sortDirection"),
            IntOf("page"),
            ValueOf("eligibility"));
    }
}
