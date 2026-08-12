using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
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
    public async Task GetParticipantRoster_ReturnsOk_WithRepeatedFiltersAndTagAnnotations()
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
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput
            {
                CampaignId = campaignId,
                GraduationYears = [2028, 2029],
                TagDefinitionIds = [tagId],
                Page = 1,
                PageSize = 50
            }),
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
    public async Task GetParticipantRoster_ReturnsNotFoundProblem_ForMissingCampaign()
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
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = 999_999, Page = 1, PageSize = 50 }),
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
    public async Task GetParticipantRoster_ReturnsValidationProblem_ForInvalidPageSize()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-roster-invalid-page-size");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = 1, Page = 1, PageSize = 101 }),
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
    public async Task GetParticipantDetail_ReturnsNotFoundProblem_ForMissingParticipant()
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
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId + 1),
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
    public async Task GetParticipantDetail_ReturnsValidationProblem_ForNonPositiveRouteValues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-detail-invalid-route");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var campaignResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(0, 1),
            cancellationToken);
        campaignResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var campaignDocument = await JsonDocument.ParseAsync(
            await campaignResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        campaignDocument.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        campaignDocument.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();

        using var assignmentResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(1, 0),
            cancellationToken);
        assignmentResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var assignmentDocument = await JsonDocument.ParseAsync(
            await assignmentResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        assignmentDocument.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        assignmentDocument.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies anonymous callers receive an unauthorized response for both participant routes.
    /// </summary>
    [Fact]
    public async Task GetParticipantRoutes_ReturnUnauthorized_ForAnonymousCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using var rosterResponse = await anonymousClient.GetAsync(
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = 1, Page = 1, PageSize = 50 }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var detailResponse = await anonymousClient.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(1, 1),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies authenticated callers without a club receive forbidden responses for both routes.
    /// </summary>
    [Fact]
    public async Task GetParticipantRoutes_ReturnForbidden_ForAuthenticatedUserWithoutClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var rosterResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = 1, Page = 1, PageSize = 50 }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var detailResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(1, 1),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a current-club member can load a participant detail payload with the expected shape.
    /// </summary>
    [Fact]
    public async Task GetParticipantDetail_ReturnsPayload_ForCurrentClubMember()
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
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(cancellationToken);
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
    public async Task GetParticipantRoutes_ReturnPayload_ForLeastPrivilegedClubMember()
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
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Page = 1, PageSize = 50 }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await rosterResponse.Content.ReadFromJsonAsync<PagedResult<CampaignParticipantRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        roster.TotalCount.ShouldBe(1);

        using var detailResponse = await memberClient.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(cancellationToken);
        detail.ShouldNotBeNull();
        detail.Notes[0].CanEdit.ShouldBeTrue();
        detail.Notes[0].CanDelete.ShouldBeTrue();
        detail.AppliedTags[0].CanRemove.ShouldBeTrue();
        detail.Capabilities.CanAddNote.ShouldBeTrue();
        detail.Capabilities.CanApplyTag.ShouldBeTrue();
        detail.Capabilities.CanEditPlacement.ShouldBeFalse();
        detail.Capabilities.CanArchiveTagDefinitions.ShouldBeFalse();
    }

    /// <summary>
    /// Verifies cross-tenant campaign and assignment IDs are rejected with non-disclosing not-found responses.
    /// </summary>
    [Fact]
    public async Task GetParticipantRoutes_ReturnNotFound_ForCrossTenantCampaignOrAssignment()
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
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Page = 1, PageSize = 50 }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var detailResponse = await currentClient.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies the roster endpoint applies default paging when both query values are omitted at the endpoint boundary.
    /// </summary>
    [Fact]
    public async Task GetParticipantRoster_AppliesDefaultPaging_WhenPageAndPageSizeAreOmitted()
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
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Page = null, PageSize = null }),
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
    public async Task GetParticipantRoster_TreatsSearchWildcardsAsLiterals_OnPostgresLikeBranch()
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
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Search = "%", Page = 1, PageSize = 50 }),
            cancellationToken);
        percentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var percentRoster = await percentResponse.Content.ReadFromJsonAsync<PagedResult<CampaignParticipantRosterItem>>(cancellationToken);
        percentRoster.ShouldNotBeNull();
        percentRoster.TotalCount.ShouldBe(1);
        percentRoster.Items[0].DisplayName.ShouldBe("A% Player");

        using var underscoreResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Search = "_", Page = 1, PageSize = 50 }),
            cancellationToken);
        underscoreResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var underscoreRoster = await underscoreResponse.Content.ReadFromJsonAsync<PagedResult<CampaignParticipantRosterItem>>(cancellationToken);
        underscoreRoster.ShouldNotBeNull();
        underscoreRoster.TotalCount.ShouldBe(1);
        underscoreRoster.Items[0].DisplayName.ShouldBe("A_ Player");

        using var backslashResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId, Search = "\\", Page = 1, PageSize = 50 }),
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
        using var response = await client.PostAsJsonAsync(
            ClubEndpoints.Create,
            new CreateClubInput { Name = $"Club {Guid.NewGuid():N}", City = "X", State = "TX" },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        user.ClubId = clubId;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<(long CampaignId, long TagId, long AssignmentId)> SeedRosterDataAsync(long clubId, string email, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        var season = new SeasonEntity { Name = "Roster Season", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = user.Id };
        var campaign = new CampaignEntity { Name = "Roster Campaign", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = 0, ClubId = clubId, CreatedById = user.Id };
        var player = new PlayerEntity { FirstName = "Avery", LastName = "Adams", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };
        var playerTag = new PlayerTagEntity { Name = "Roster Tag", Color = "Blue", ClubId = clubId, CreatedById = user.Id, LifecycleStatus = LifecycleStatus.Active };

        context.AddRange(season, campaign, player, playerTag);
        await context.SaveChangesAsync(cancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity { PlayerId = player.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.Undecided, TryoutNumber = 7 };
        context.Add(assignment);
        await context.SaveChangesAsync(cancellationToken);

        context.CampaignTagApplications.Add(new CampaignTagApplicationEntity { PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId, PlayerTagId = playerTag.PlayerTagId, ClubId = clubId, CreatedById = user.Id });
        context.Notes.Add(new NoteEntity { PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId, ClubId = clubId, Content = "Roster note", CreatedById = user.Id });
        await context.SaveChangesAsync(cancellationToken);

        return (campaign.CampaignId, playerTag.PlayerTagId, assignment.PlayerCampaignAssignmentId);
    }

    private async Task<long> SeedWildcardSearchDataAsync(long clubId, string email, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        var season = new SeasonEntity { Name = "Wildcard Search Season", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = user.Id };
        var campaign = new CampaignEntity { Name = "Wildcard Search Campaign", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = 0, ClubId = clubId, CreatedById = user.Id };
        context.AddRange(season, campaign);
        await context.SaveChangesAsync(cancellationToken);

        var players = new[]
        {
            new PlayerEntity { FirstName = "A%", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id },
            new PlayerEntity { FirstName = "A_", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id },
            new PlayerEntity { FirstName = "A\\", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id }
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
