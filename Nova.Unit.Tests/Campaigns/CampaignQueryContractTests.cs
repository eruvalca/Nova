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

    /// <summary>Verifies the detail URL builder routes to the shared route.</summary>
    [Fact]
    public void GetCampaignDetailUrl_BuildsExpectedUrl()
    {
        var url = CampaignEndpoints.GetCampaignDetailUrl(42);

        url.ShouldBe("/api/campaigns/42");
    }

    /// <summary>Verifies non-positive campaign identifiers are rejected.</summary>
    /// <param name="campaignId">The campaign identifier to validate.</param>
    /// <param name="isValid">Whether the identifier is valid.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    public void GetCampaignDetailInput_ValidatesCampaignIdBounds(long campaignId, bool isValid)
    {
        var errors = InputValidator.Validate(new GetCampaignDetailInput { CampaignId = campaignId });

        errors.ContainsKey(nameof(GetCampaignDetailInput.CampaignId)).ShouldBe(!isValid);
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
    [Theory(IncludeTestCaseIndex = true)]
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
    /// Verifies both accepted campaign status values are case-insensitive.
    /// </summary>
    /// <param name="status">The status spelling to validate.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("active")]
    [InlineData("ACTIVE")]
    [InlineData("closed")]
    [InlineData("CLOSED")]
    public void GetCampaignListInput_AcceptsSupportedStatusCaseInsensitively(string status)
    {
        var errors = InputValidator.Validate(new GetCampaignListInput { Status = status });

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies the roster URL builder normalizes accepted filters and sorts.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantRosterUrl_BuildsExpectedUrl()
    {
        var url = CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput
        {
            CampaignId = 42,
            Search = " A ",
            GraduationYears = new[] { 2028, 2029 },
            TagDefinitionIds = new[] { 7L, 8L },
            Outcome = " ASSIGNED ",
            TeamId = 9,
            SortBy = " GRADUATIONYEAR ",
            SortDirection = " DESC ",
            Page = 2,
            PageSize = 25
        });

        url.ShouldBe("/api/campaigns/42/participants?search=A&graduationYears=2028&graduationYears=2029&tagDefinitionIds=7&tagDefinitionIds=8&outcome=assigned&teamId=9&sortBy=graduationYear&sortDirection=desc&page=2&pageSize=25");
    }

    /// <summary>
    /// Verifies unsupported outcome/sort values are omitted while paging is still forwarded
    /// so server-side bounds validation can reject out-of-range page sizes.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantRosterUrl_OmitsInvalidOptionalValues()
    {
        var url = CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput
        {
            CampaignId = 42,
            Search = " ",
            Outcome = " invalid ",
            TeamId = 0,
            SortBy = " invalid ",
            SortDirection = " invalid ",
            Page = 1,
            PageSize = 101
        });

        url.ShouldBe("/api/campaigns/42/participants?page=1&pageSize=101");
    }

    /// <summary>
    /// Verifies filter-element values are reflected faithfully rather than silently broadened;
    /// the shared input validation is what rejects non-positive elements before a request is made.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantRosterUrl_ForwardsFilterElementsForServerValidation()
    {
        var url = CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput
        {
            CampaignId = 42,
            GraduationYears = new[] { 2028, 2028, 0 },
            TagDefinitionIds = new[] { 7L, 0L }
        });

        url.ShouldBe("/api/campaigns/42/participants?graduationYears=2028&graduationYears=0&tagDefinitionIds=7&tagDefinitionIds=0&page=1&pageSize=50");
    }

    /// <summary>
    /// Verifies the placement roster URL builder omits page sizes rejected by the input contract.
    /// </summary>
    [Fact]
    public void GetCampaignPlacementRosterUrl_OmitsOutOfRangePageSize()
    {
        var url = CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
        {
            CampaignId = 42,
            PageSize = GetCampaignPlacementRosterInput.MaxPageSize + 1
        });

        url.ShouldBe("/api/campaigns/42/placements?page=1");
    }

    /// <summary>
    /// Verifies non-positive filter elements are rejected by the shared input validation.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantRosterInput_RejectsNonPositiveFilterElements()
    {
        var errors = InputValidator.Validate(new GetCampaignParticipantRosterInput
        {
            CampaignId = 1,
            GraduationYears = new[] { 0 },
            TagDefinitionIds = new[] { 0L }
        });

        errors.ShouldContainKey(nameof(GetCampaignParticipantRosterInput.GraduationYears));
        errors.ShouldContainKey(nameof(GetCampaignParticipantRosterInput.TagDefinitionIds));
    }

    /// <summary>
    /// Verifies positive filter elements satisfy the shared input validation.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantRosterInput_AcceptsPositiveFilterElements()
    {
        var errors = InputValidator.Validate(new GetCampaignParticipantRosterInput
        {
            CampaignId = 1,
            GraduationYears = new[] { 2028, 2029 },
            TagDefinitionIds = new[] { 7L, 8L }
        });

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies the roster input applies default paging when omitted.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantRosterInput_DefaultsToPageOneAndPageSizeFifty_WhenOmitted()
    {
        var input = new GetCampaignParticipantRosterInput { CampaignId = 1 };

        input.Page.ShouldBe(GetCampaignParticipantRosterInput.DefaultPage);
        input.PageSize.ShouldBe(GetCampaignParticipantRosterInput.DefaultPageSize);
        InputValidator.Validate(input).ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies both accepted sort directions are case-insensitive.
    /// </summary>
    /// <param name="direction">The direction spelling to validate.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("asc")]
    [InlineData("ASC")]
    [InlineData("desc")]
    [InlineData("DESC")]
    public void GetCampaignParticipantRosterInput_AcceptsSupportedSortDirectionsCaseInsensitively(string direction)
    {
        var input = new GetCampaignParticipantRosterInput { CampaignId = 1, SortDirection = direction };

        InputValidator.Validate(input).ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies the graduation-years URL builder produces the shared route shape.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantGraduationYearsUrl_BuildsExpectedUrl()
    {
        CampaignEndpoints.GetCampaignParticipantGraduationYearsUrl(42)
            .ShouldBe("/api/campaigns/42/participants/graduation-years");
    }

    /// <summary>
    /// Verifies the graduation-years route constant matches the URL builder output.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantGraduationYears_ConstantMatchesUrlBuilder()
    {
        var url = CampaignEndpoints.GetCampaignParticipantGraduationYearsUrl(42);

        url.ShouldBe(CampaignEndpoints.GetCampaignParticipantGraduationYears.Replace("{campaignId:long}", "42"));
        CampaignEndpoints.GetCampaignParticipantGraduationYearsRelative.ShouldBe("{campaignId:long}/participants/graduation-years");
        CampaignEndpoints.GetCampaignParticipantGraduationYearsRouteName.ShouldBe("GetCampaignParticipantGraduationYears");
    }

    /// <summary>
    /// Verifies the graduation-years input rejects a non-positive campaign identifier.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantGraduationYearsInput_RejectsNonPositiveCampaignId()
    {
        var errors = InputValidator.Validate(new GetCampaignParticipantGraduationYearsInput { CampaignId = 0 });

        errors.ShouldContainKey(nameof(GetCampaignParticipantGraduationYearsInput.CampaignId));
    }

    /// <summary>
    /// Verifies the graduation-years input accepts a positive campaign identifier.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantGraduationYearsInput_AcceptsPositiveCampaignId()
    {
        var errors = InputValidator.Validate(new GetCampaignParticipantGraduationYearsInput { CampaignId = 42 });

        errors.ShouldBeEmpty();
    }
}
