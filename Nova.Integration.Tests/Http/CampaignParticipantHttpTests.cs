using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for campaign participant roster and detail endpoints.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignParticipantHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies the roster endpoint accepts repeated query values and returns the expected payload.
    /// </summary>
    [Fact]
    public async Task GetParticipantRosterReturnsOkWithRepeatedFiltersAndTagAnnotationsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-roster");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (campaignId, tagId, _) = await SeedRosterDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput
{
    CampaignId = campaignId,
    GraduationYears = [2028, 2029],
    TagDefinitionIds = [tagId],
    Page = 1,
    PageSize = 50
}), UriKind.RelativeOrAbsolute),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await response.Content.ReadFromJsonAsync<PagedResult<CampaignParticipantRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        roster.TotalCount.ShouldBe(1);
        roster.Items.Count.ShouldBe(1);
        roster.Items[0].DisplayName.ShouldBe("Avery Adams");
        roster.Items[0].AppliedTags.ShouldContain(tag => tag.PlayerTagId == tagId);
    }

    /// <summary>
    /// Verifies the roster endpoint returns non-disclosing not-found ProblemDetails for missing campaigns.
    /// </summary>
    [Fact]
    public async Task GetParticipantRosterReturnsNotFoundProblemForMissingCampaignAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-roster-missing-campaign");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        await SeedRosterDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = 999_999, Page = 1, PageSize = 50 }), UriKind.RelativeOrAbsolute),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.NotFound);
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies the roster endpoint rejects invalid explicit page-size values before the handler runs.
    /// </summary>
    [Fact]
    public async Task GetParticipantRosterReturnsValidationProblemForInvalidPageSizeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-roster-invalid-page-size");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = 1, Page = 1, PageSize = 101 }), UriKind.RelativeOrAbsolute),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies the detail endpoint returns non-disclosing not-found ProblemDetails for missing participants.
    /// </summary>
    [Fact]
    public async Task GetParticipantDetailReturnsNotFoundProblemForMissingParticipantAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-detail");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (campaignId, _, assignmentId) = await SeedRosterDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId + 1), UriKind.RelativeOrAbsolute),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.NotFound);
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies the detail endpoint rejects non-positive route values with validation ProblemDetails.
    /// </summary>
    [Fact]
    public async Task GetParticipantDetailReturnsValidationProblemForNonPositiveRouteValuesAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-detail-invalid-route");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var campaignResponse = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantDetailUrl(0, 1), UriKind.RelativeOrAbsolute),
            cancellationToken);
        campaignResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var campaignDocument = await JsonDocument.ParseAsync(
            await campaignResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        campaignDocument.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        campaignDocument.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();

        using var assignmentResponse = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantDetailUrl(1, 0), UriKind.RelativeOrAbsolute),
            cancellationToken);
        assignmentResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var assignmentDocument = await JsonDocument.ParseAsync(
            await assignmentResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        assignmentDocument.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        assignmentDocument.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies the graduation-years endpoint returns the roster's distinct years in ascending order.
    /// </summary>
    [Fact]
    public async Task GetParticipantGraduationYearsReturnsAscendingYearsForCurrentClubMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-graduation-years");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (campaignId, _, _) = await SeedRosterDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantGraduationYearsUrl(campaignId), UriKind.RelativeOrAbsolute),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var years = await response.Content.ReadFromJsonAsync<List<int>>(cancellationToken);
        years.ShouldNotBeNull();
        years.ShouldBe([2028]);
    }

    /// <summary>
    /// Verifies the graduation-years endpoint rejects a non-positive campaign identifier with validation ProblemDetails.
    /// </summary>
    [Fact]
    public async Task GetParticipantGraduationYearsReturnsValidationProblemForNonPositiveRouteValueAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-graduation-years-invalid-route");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantGraduationYearsUrl(0), UriKind.RelativeOrAbsolute),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies anonymous callers receive an unauthorized response for the participant routes.
    /// </summary>
    [Fact]
    public async Task GetParticipantRoutesReturnUnauthorizedForAnonymousCallerAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using var rosterResponse = await anonymousClient.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = 1, Page = 1, PageSize = 50 }), UriKind.RelativeOrAbsolute),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var detailResponse = await anonymousClient.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantDetailUrl(1, 1), UriKind.RelativeOrAbsolute),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var graduationYearsResponse = await anonymousClient.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantGraduationYearsUrl(1), UriKind.RelativeOrAbsolute),
            cancellationToken);
        graduationYearsResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies authenticated callers without a club receive forbidden responses for both routes.
    /// </summary>
    [Fact]
    public async Task GetParticipantRoutesReturnForbiddenForAuthenticatedUserWithoutClubAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var rosterResponse = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = 1, Page = 1, PageSize = 50 }), UriKind.RelativeOrAbsolute),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var detailResponse = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantDetailUrl(1, 1), UriKind.RelativeOrAbsolute),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var graduationYearsResponse = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantGraduationYearsUrl(1), UriKind.RelativeOrAbsolute),
            cancellationToken);
        graduationYearsResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a current-club member can load a participant detail payload with the expected shape.
    /// </summary>
    [Fact]
    public async Task GetParticipantDetailReturnsPayloadForCurrentClubMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-detail-success");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (campaignId, _, assignmentId) = await SeedRosterDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId), UriKind.RelativeOrAbsolute),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await EvaluationEvidenceHttpTestSupport.ReadEvaluationEvidenceAsync(response, client, cancellationToken);
        detail.ShouldNotBeNull();
        detail.PlayerCampaignAssignmentId.ShouldBe(assignmentId);
        detail.Notes.Count.ShouldBe(1);
        detail.Notes[0].CanEdit.ShouldBeTrue();
        detail.Notes[0].CanDelete.ShouldBeTrue();
        detail.AppliedTags[0].CanRemove.ShouldBeTrue();
        detail.Capabilities.CanAddNote.ShouldBeTrue();
        detail.Capabilities.CanApplyTag.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies a least-privileged club member (without the ClubAdmin role) can load both participant routes.
    /// </summary>
    [Fact]
    public async Task GetParticipantRoutesReturnPayloadForLeastPrivilegedClubMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // The club creator becomes a ClubAdmin by the create-club flow, so a second
        // non-admin user is required to prove ordinary member access to both routes.
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("participant-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("participant-least-privileged");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);
        var (campaignId, _, assignmentId) = await SeedRosterDataAsync(club.ClubId, memberEmail, cancellationToken);

        using var rosterResponse = await memberClient.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Page = 1, PageSize = 50 }), UriKind.RelativeOrAbsolute),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await rosterResponse.Content.ReadFromJsonAsync<PagedResult<CampaignParticipantRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        roster.TotalCount.ShouldBe(1);

        using var detailResponse = await memberClient.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId), UriKind.RelativeOrAbsolute),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await EvaluationEvidenceHttpTestSupport.ReadEvaluationEvidenceAsync(detailResponse, memberClient, cancellationToken);
        detail.ShouldNotBeNull();
        detail.Notes[0].CanEdit.ShouldBeTrue();
        detail.Notes[0].CanDelete.ShouldBeTrue();
        detail.AppliedTags[0].CanRemove.ShouldBeTrue();
        detail.Capabilities.CanAddNote.ShouldBeTrue();
        detail.Capabilities.CanApplyTag.ShouldBeTrue();
        detail.Capabilities.CanEditPlacement.ShouldBeTrue();
        detail.Capabilities.CanArchiveTagDefinitions.ShouldBeFalse();
    }

    /// <summary>
    /// Verifies cross-tenant campaign and assignment IDs are rejected with non-disclosing not-found responses.
    /// </summary>
    [Fact]
    public async Task GetParticipantRoutesReturnNotFoundForCrossTenantCampaignOrAssignmentAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var currentClient = fixture.CreateNovaHttpClient();
        var currentEmail = UniqueEmail("participant-cross-tenant-current");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(currentClient, currentEmail, Password, cancellationToken);
        await UpdateUserAsync(currentEmail, clubId: null, cancellationToken);
        await CreateClubAsync(currentClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(currentClient, cancellationToken);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("participant-cross-tenant-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        var (campaignId, _, assignmentId) = await SeedRosterDataAsync(otherClub.ClubId, otherEmail, cancellationToken);

        using var rosterResponse = await currentClient.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Page = 1, PageSize = 50 }), UriKind.RelativeOrAbsolute),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var detailResponse = await currentClient.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId), UriKind.RelativeOrAbsolute),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var graduationYearsResponse = await currentClient.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantGraduationYearsUrl(campaignId), UriKind.RelativeOrAbsolute),
            cancellationToken);
        graduationYearsResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies the roster endpoint applies default paging when both query values are omitted at the endpoint boundary.
    /// </summary>
    [Fact]
    public async Task GetParticipantRosterAppliesDefaultPagingWhenPageAndPageSizeAreOmittedAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-roster-default-paging");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (campaignId, _, _) = await SeedRosterDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Page = null, PageSize = null }), UriKind.RelativeOrAbsolute),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await response.Content.ReadFromJsonAsync<PagedResult<CampaignParticipantRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        roster.Page.ShouldBe(GetCampaignParticipantRosterInput.DefaultPage);
        roster.PageSize.ShouldBe(GetCampaignParticipantRosterInput.DefaultPageSize);
        roster.TotalCount.ShouldBe(1);
        roster.Items.Count.ShouldBe(1);
    }

    /// <summary>
    /// Verifies wildcard characters are treated as literals when the PostgreSQL search branch is used.
    /// </summary>
    [Fact]
    public async Task GetParticipantRosterTreatsSearchWildcardsAsLiteralsOnPostgresLikeBranchAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-wildcards");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var campaignId = await SeedWildcardSearchDataAsync(club.ClubId, email, cancellationToken);

        using var percentResponse = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Search = "%", Page = 1, PageSize = 50 }), UriKind.RelativeOrAbsolute),
            cancellationToken);
        percentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var percentRoster = await percentResponse.Content.ReadFromJsonAsync<PagedResult<CampaignParticipantRosterItem>>(cancellationToken);
        percentRoster.ShouldNotBeNull();
        percentRoster.TotalCount.ShouldBe(1);
        percentRoster.Items[0].DisplayName.ShouldBe("A% Player");

        using var underscoreResponse = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Search = "_", Page = 1, PageSize = 50 }), UriKind.RelativeOrAbsolute),
            cancellationToken);
        underscoreResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var underscoreRoster = await underscoreResponse.Content.ReadFromJsonAsync<PagedResult<CampaignParticipantRosterItem>>(cancellationToken);
        underscoreRoster.ShouldNotBeNull();
        underscoreRoster.TotalCount.ShouldBe(1);
        underscoreRoster.Items[0].DisplayName.ShouldBe("A_ Player");

        using var backslashResponse = await client.GetAsync(
new Uri(CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Search = "\\", Page = 1, PageSize = 50 }), UriKind.RelativeOrAbsolute),
            cancellationToken);
        backslashResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var backslashRoster = await backslashResponse.Content.ReadFromJsonAsync<PagedResult<CampaignParticipantRosterItem>>(cancellationToken);
        backslashRoster.ShouldNotBeNull();
        backslashRoster.TotalCount.ShouldBe(1);
        backslashRoster.Items[0].DisplayName.ShouldBe("A\\ Player");
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    private static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var responseRequestContent = SeedingHelpers.CreateClubMultipartContent($"Club {Guid.NewGuid():N}", "X", "TX");
        using var response = await client.PostAsync(
        new Uri(ClubEndpoints.Create, UriKind.RelativeOrAbsolute),
                    responseRequestContent,
                    cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"{ClubEndpoints.Complete}?returnUrl=/dashboard", UriKind.RelativeOrAbsolute), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
    {
        var context = fixture.CreateAdminContext();
        await using (context)
        {
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
#pragma warning restore CA1862
            user.ClubId = clubId;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<(long CampaignId, long TagId, long AssignmentId)> SeedRosterDataAsync(long clubId, string email, CancellationToken cancellationToken)
    {
        var context = fixture.CreateAdminContext();
        await using (context)
        {
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
#pragma warning restore CA1862
            var season = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "Roster Season", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = user.Id };
            var campaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Roster Campaign", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = 0, ClubId = clubId, CreatedById = user.Id };
            var player = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Avery", LastName = "Adams", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };
            var playerTag = new PlayerTagEntity { CreationOperationId = Guid.NewGuid(), Name = "Roster Tag", NormalizedName = "ROSTER TAG", Color = "Blue", ClubId = clubId, CreatedById = user.Id, LifecycleStatus = LifecycleStatus.Active };

            context.AddRange(season, campaign, player, playerTag);
            await context.SaveChangesAsync(cancellationToken);

            var assignment = new PlayerCampaignAssignmentEntity { PlayerId = player.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.Undecided, TryoutNumber = 7 };
            context.Add(assignment);
            await context.SaveChangesAsync(cancellationToken);

            context.CampaignTagApplications.Add(new CampaignTagApplicationEntity { CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId, PlayerTagId = playerTag.PlayerTagId, ClubId = clubId, CreatedById = user.Id });
            context.Notes.Add(new NoteEntity { CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId, ClubId = clubId, Content = "Roster note", CreatedById = user.Id });
            await context.SaveChangesAsync(cancellationToken);

            return (campaign.CampaignId, playerTag.PlayerTagId, assignment.PlayerCampaignAssignmentId);
        }
    }

    private async Task<long> SeedWildcardSearchDataAsync(long clubId, string email, CancellationToken cancellationToken)
    {
        var context = fixture.CreateAdminContext();
        await using (context)
        {
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
#pragma warning restore CA1862
            var season = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "Wildcard Search Season", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = user.Id };
            var campaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Wildcard Search Campaign", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = 0, ClubId = clubId, CreatedById = user.Id };
            context.AddRange(season, campaign);
            await context.SaveChangesAsync(cancellationToken);

            var players = new[]
            {
            new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "A%", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id },
            new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "A_", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id },
            new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "A\\", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id }
        };

            context.Players.AddRange(players);
            await context.SaveChangesAsync(cancellationToken);

            context.PlayerCampaignAssignments.AddRange(
                players.Select((player, index) => new PlayerCampaignAssignmentEntity
                {
                    PlayerId = player.PlayerId,
                    CampaignId = campaign.CampaignId,
                    ClubId = clubId,
                    CreatedById = user.Id,
                    PlacementOutcome = PlacementOutcome.Undecided,
                    TryoutNumber = index + 1
                }));
            await context.SaveChangesAsync(cancellationToken);

            return campaign.CampaignId;
        }
    }
}
