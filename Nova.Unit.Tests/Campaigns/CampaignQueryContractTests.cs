using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies shared campaign query validation and route contracts.
/// </summary>
public sealed class CampaignQueryContractTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, "/api/campaigns/42/placements?page=1&pageSize=50")]
    [InlineData(-1L, "/api/campaigns/42/placements?page=1&pageSize=50")]
    [InlineData(0L, "/api/campaigns/42/placements?page=1&pageSize=50")]
    [InlineData(1L, "/api/campaigns/42/placements?participantId=1&page=1&pageSize=50")]
    [InlineData(long.MaxValue, "/api/campaigns/42/placements?participantId=9223372036854775807&page=1&pageSize=50")]
    public void PlacementRosterUrlIncludesOnlyPositiveParticipantIds(long? participantId, string expected)
    {
        CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
        {
            CampaignId = 42,
            ParticipantId = participantId
        }).ShouldBe(expected);
    }

    /// <summary>Verifies campaign pages are one-based, including the largest supported integer.</summary>
    /// <param name="page">The requested page.</param>
    /// <param name="isValid">Whether the page satisfies the contract.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(int.MaxValue, true)]
    public void GetCampaignListInputValidatesPageBounds(int page, bool isValid)
    {
        var errors = InputValidator.Validate(new GetCampaignListInput { Page = page });

        errors.ContainsKey(nameof(GetCampaignListInput.Page)).ShouldBe(!isValid);
    }

    /// <summary>Verifies accepted filters produce the expected normalized URL.</summary>
    [Fact]
    public void GetCampaignListUrlBuildsExpectedUrl()
    {
        var url = CampaignEndpoints.GetCampaignListUrl(" Active ", 25);

        url.ShouldBe("/api/campaigns?status=active&limit=25");
    }

    /// <summary>Verifies directory continuation forwards its page together with filters and bounds.</summary>
    [Fact]
    public void GetCampaignListUrlIncludesRequestedPage()
    {
        CampaignEndpoints.GetCampaignListUrl("draft", 20, 2)
            .ShouldBe("/api/campaigns?page=2&status=draft&limit=20");
    }

    /// <summary>Verifies omitted filters satisfy the shared input contract.</summary>
    [Fact]
    public void GetCampaignListInputDefaultsToNoValidationErrorsWhenOmitted()
    {
        var errors = InputValidator.Validate(new GetCampaignListInput());
        errors.ShouldBeEmpty();
    }

    /// <summary>Verifies the detail URL builder routes to the shared route.</summary>
    [Fact]
    public void GetCampaignDetailUrlBuildsExpectedUrl()
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
    public void GetCampaignDetailInputValidatesCampaignIdBounds(long campaignId, bool isValid)
    {
        var errors = InputValidator.Validate(new GetCampaignDetailInput { CampaignId = campaignId });

        errors.ContainsKey(nameof(GetCampaignDetailInput.CampaignId)).ShouldBe(!isValid);
    }

    /// <summary>Verifies unsupported status values are rejected.</summary>
    [Fact]
    public void GetCampaignListInputRejectsInvalidStatus()
    {
        var errors = InputValidator.Validate(new GetCampaignListInput { Status = "open" });
        errors.ShouldContainKey(nameof(GetCampaignListInput.Status));
    }

    /// <summary>Verifies an explicitly empty status is rejected.</summary>
    [Fact]
    public void GetCampaignListInputRejectsEmptyStatus()
    {
        var errors = InputValidator.Validate(new GetCampaignListInput { Status = string.Empty });
        errors.ShouldContainKey(nameof(GetCampaignListInput.Status));
    }

    /// <summary>Verifies invalid optional values are omitted by the URL builder.</summary>
    [Fact]
    public void GetCampaignListUrlOmitsInvalidOptionalValues()
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
    public void GetCampaignListInputValidatesLimitBounds(int limit, bool isValid)
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
    public void GetCampaignListInputAcceptsSupportedStatusCaseInsensitively(string status)
    {
        var errors = InputValidator.Validate(new GetCampaignListInput { Status = status });

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies the roster URL builder normalizes accepted filters and sorts.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantRosterUrlBuildsExpectedUrl()
    {
        var url = CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput
        {
            CampaignId = 42,
            Search = " A ",
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
            GraduationYears = new[] { 2028, 2029 },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
            TagDefinitionIds = new[] { 7L, 8L },
#pragma warning restore CA1861
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
    public void GetCampaignParticipantRosterUrlOmitsInvalidOptionalValues()
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
    public void GetCampaignParticipantRosterUrlForwardsFilterElementsForServerValidation()
    {
        var url = CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput
        {
            CampaignId = 42,
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
            GraduationYears = new[] { 2028, 2028, 0 },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
            TagDefinitionIds = new[] { 7L, 0L }
#pragma warning restore CA1861
        });

        url.ShouldBe("/api/campaigns/42/participants?graduationYears=2028&graduationYears=0&tagDefinitionIds=7&tagDefinitionIds=0&page=1&pageSize=50");
    }

    /// <summary>
    /// Verifies the placement roster URL builder omits page sizes rejected by the input contract.
    /// </summary>
    [Fact]
    public void GetCampaignPlacementRosterUrlOmitsOutOfRangePageSize()
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
    public void GetCampaignParticipantRosterInputRejectsNonPositiveFilterElements()
    {
        var errors = InputValidator.Validate(new GetCampaignParticipantRosterInput
        {
            CampaignId = 1,
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
            GraduationYears = new[] { 0 },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
            TagDefinitionIds = new[] { 0L }
#pragma warning restore CA1861
        });

        errors.ShouldContainKey(nameof(GetCampaignParticipantRosterInput.GraduationYears));
        errors.ShouldContainKey(nameof(GetCampaignParticipantRosterInput.TagDefinitionIds));
    }

    /// <summary>
    /// Verifies positive filter elements satisfy the shared input validation.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantRosterInputAcceptsPositiveFilterElements()
    {
        var errors = InputValidator.Validate(new GetCampaignParticipantRosterInput
        {
            CampaignId = 1,
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
            GraduationYears = new[] { 2028, 2029 },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
            TagDefinitionIds = new[] { 7L, 8L }
#pragma warning restore CA1861
        });

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies the roster input applies default paging when omitted.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantRosterInputDefaultsToPageOneAndPageSizeFiftyWhenOmitted()
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
    public void GetCampaignParticipantRosterInputAcceptsSupportedSortDirectionsCaseInsensitively(string direction)
    {
        var input = new GetCampaignParticipantRosterInput { CampaignId = 1, SortDirection = direction };

        InputValidator.Validate(input).ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies the graduation-years URL builder produces the shared route shape.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantGraduationYearsUrlBuildsExpectedUrl()
    {
        CampaignEndpoints.GetCampaignParticipantGraduationYearsUrl(42)
            .ShouldBe("/api/campaigns/42/participants/graduation-years");
    }

    /// <summary>
    /// Verifies the graduation-years route constant matches the URL builder output.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantGraduationYearsConstantMatchesUrlBuilder()
    {
        var url = CampaignEndpoints.GetCampaignParticipantGraduationYearsUrl(42);

        url.ShouldBe(CampaignEndpoints.GetCampaignParticipantGraduationYears.Replace("{campaignId:long}", "42", StringComparison.Ordinal));
        CampaignEndpoints.GetCampaignParticipantGraduationYearsRelative.ShouldBe("{campaignId:long}/participants/graduation-years");
        CampaignEndpoints.GetCampaignParticipantGraduationYearsRouteName.ShouldBe("GetCampaignParticipantGraduationYears");
    }

    /// <summary>
    /// Verifies the graduation-years input rejects a non-positive campaign identifier.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantGraduationYearsInputRejectsNonPositiveCampaignId()
    {
        var errors = InputValidator.Validate(new GetCampaignParticipantGraduationYearsInput { CampaignId = 0 });

        errors.ShouldContainKey(nameof(GetCampaignParticipantGraduationYearsInput.CampaignId));
    }

    /// <summary>
    /// Verifies the graduation-years input accepts a positive campaign identifier.
    /// </summary>
    [Fact]
    public void GetCampaignParticipantGraduationYearsInputAcceptsPositiveCampaignId()
    {
        var errors = InputValidator.Validate(new GetCampaignParticipantGraduationYearsInput { CampaignId = 42 });

        errors.ShouldBeEmpty();
    }
}
