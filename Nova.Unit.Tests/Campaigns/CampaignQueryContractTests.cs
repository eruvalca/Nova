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
    /// Verifies both accepted campaign status values are case-insensitive.
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
            GraduationYears = new[] { 2028, 2029, 0 },
            TagDefinitionIds = new[] { 7L, 8L, -1L },
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
            GraduationYears = new[] { 0 },
            TagDefinitionIds = new[] { 0L },
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
    [Theory]
    [InlineData("asc")]
    [InlineData("ASC")]
    [InlineData("desc")]
    [InlineData("DESC")]
    public void GetCampaignParticipantRosterInput_AcceptsSupportedSortDirectionsCaseInsensitively(string direction)
    {
        var input = new GetCampaignParticipantRosterInput { CampaignId = 1, SortDirection = direction };

        InputValidator.Validate(input).ShouldBeEmpty();
    }
}
