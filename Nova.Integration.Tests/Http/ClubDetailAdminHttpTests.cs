using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Shared.Clubs;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for club detail/admin page access and join-request workflows.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public class ClubDetailAdminHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies login-vs-access-denied distinctions and member/admin access on the club detail page.
    /// </summary>
    [Fact]
    public async Task ClubDetailRoute_UsesLoginOrAccessDeniedAndAllowsMembersAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();
        using var clubAdminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        using var noClubClient = fixture.CreateNovaHttpClient();
        using var otherClubAdminClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("club-detail-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAdminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Casey", "Captain", clubId: null, cancellationToken);
        var club = await CreateClubAsync(clubAdminClient, "Austin Arrows", "Austin", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAdminClient, cancellationToken);

        var memberEmail = UniqueEmail("club-detail-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, "Mia", "Member", club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        var noClubEmail = UniqueEmail("club-detail-noclub");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(noClubClient, noClubEmail, Password, cancellationToken);
        await UpdateUserAsync(noClubEmail, "Nora", "NoClub", clubId: null, cancellationToken);

        var otherClubAdminEmail = UniqueEmail("club-detail-other-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClubAdminClient, otherClubAdminEmail, Password, cancellationToken);
        await UpdateUserAsync(otherClubAdminEmail, "Otto", "OtherAdmin", clubId: null, cancellationToken);
        _ = await CreateClubAsync(otherClubAdminClient, "Boston Bears", "Boston", "MA", cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClubAdminClient, cancellationToken);

        var detailRoute = $"/Clubs/{club.ClubId}";

        using (var anonymousResponse = await anonymousClient.GetAsync(detailRoute, cancellationToken))
        {
            anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
            anonymousResponse.Headers.Location.ShouldNotBeNull();
            anonymousResponse.Headers.Location.OriginalString.ShouldContain("/Account/Login");
        }

        using (var noClubResponse = await noClubClient.GetAsync(detailRoute, cancellationToken))
        {
            noClubResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
            noClubResponse.Headers.Location.ShouldNotBeNull();
            noClubResponse.Headers.Location.OriginalString.ShouldContain("/Account/AccessDenied");
        }

        using (var memberResponse = await memberClient.GetAsync(detailRoute, cancellationToken))
        {
            memberResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var adminResponse = await clubAdminClient.GetAsync(detailRoute, cancellationToken))
        {
            adminResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var otherClubAdminResponse = await otherClubAdminClient.GetAsync(detailRoute, cancellationToken))
        {
            otherClubAdminResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
            otherClubAdminResponse.Headers.Location.ShouldNotBeNull();
            otherClubAdminResponse.Headers.Location.OriginalString.ShouldContain("/Account/AccessDenied");
        }
    }

    /// <summary>
    /// Verifies login-vs-access-denied distinctions and ClubAdmin-only access on the admin page.
    /// </summary>
    [Fact]
    public async Task ClubAdminRoute_UsesLoginOrAccessDeniedAndAllowsOnlyClubAdminsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();
        using var clubAdminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        using var noClubClient = fixture.CreateNovaHttpClient();
        using var otherClubAdminClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("club-admin-route-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAdminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Avery", "Admin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(clubAdminClient, "Denver Dash", "Denver", "CO", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAdminClient, cancellationToken);

        var memberEmail = UniqueEmail("club-admin-route-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, "Mona", "Member", club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        var noClubEmail = UniqueEmail("club-admin-route-noclub");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(noClubClient, noClubEmail, Password, cancellationToken);
        await UpdateUserAsync(noClubEmail, "Neil", "NoClub", clubId: null, cancellationToken);

        var otherClubAdminEmail = UniqueEmail("club-admin-route-other-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClubAdminClient, otherClubAdminEmail, Password, cancellationToken);
        await UpdateUserAsync(otherClubAdminEmail, "Olivia", "OtherAdmin", clubId: null, cancellationToken);
        _ = await CreateClubAsync(otherClubAdminClient, "Seattle Strikers", "Seattle", "WA", cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClubAdminClient, cancellationToken);

        var adminRoute = $"/Clubs/{club.ClubId}/admin";

        using (var anonymousResponse = await anonymousClient.GetAsync(adminRoute, cancellationToken))
        {
            anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
            anonymousResponse.Headers.Location.ShouldNotBeNull();
            anonymousResponse.Headers.Location.OriginalString.ShouldContain("/Account/Login");
        }

        using (var noClubResponse = await noClubClient.GetAsync(adminRoute, cancellationToken))
        {
            noClubResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
            noClubResponse.Headers.Location.ShouldNotBeNull();
            noClubResponse.Headers.Location.OriginalString.ShouldContain("/Account/AccessDenied");
        }

        using (var memberResponse = await memberClient.GetAsync(adminRoute, cancellationToken))
        {
            memberResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
            memberResponse.Headers.Location.ShouldNotBeNull();
            memberResponse.Headers.Location.OriginalString.ShouldContain("/Account/AccessDenied");
        }

        using (var adminResponse = await clubAdminClient.GetAsync(adminRoute, cancellationToken))
        {
            adminResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var otherClubAdminResponse = await otherClubAdminClient.GetAsync(adminRoute, cancellationToken))
        {
            otherClubAdminResponse.StatusCode.ShouldBe(HttpStatusCode.Found);
            otherClubAdminResponse.Headers.Location.ShouldNotBeNull();
            otherClubAdminResponse.Headers.Location.OriginalString.ShouldContain("/Account/AccessDenied");
        }
    }

    /// <summary>
    /// Verifies member detail-page rendering excludes the admin link and includes location/roster details.
    /// </summary>
    [Fact]
    public async Task ClubDetailPage_ForMember_ShowsLocationRosterAndCurrentUserWithoutAdminLinkAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAdminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("club-detail-member-view-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAdminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Adrian", "Admin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(clubAdminClient, "Phoenix Flyers", "Phoenix", "AZ", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAdminClient, cancellationToken);

        var memberEmail = UniqueEmail("club-detail-member-view-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, "Megan", "Member", club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        using var response = await memberClient.GetAsync($"/Clubs/{club.ClubId}", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        body.ShouldContain("Phoenix, AZ");
        body.ShouldContain("Adrian Admin");
        body.ShouldContain("Megan Member");
        body.ShouldContain("Current user (You)");
        body.ShouldNotContain($"/Clubs/{club.ClubId}/admin");
    }

    /// <summary>
    /// Verifies ClubAdmin detail-page rendering includes the club-admin link.
    /// </summary>
    [Fact]
    public async Task ClubDetailPage_ForClubAdmin_ShowsAdminLinkAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAdminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("club-detail-admin-view-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAdminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Carla", "Captain", clubId: null, cancellationToken);
        var club = await CreateClubAsync(clubAdminClient, "Raleigh Rockets", "Raleigh", "NC", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAdminClient, cancellationToken);

        var memberEmail = UniqueEmail("club-detail-admin-view-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, "Rita", "Roster", club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        using var response = await clubAdminClient.GetAsync($"/Clubs/{club.ClubId}", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        body.ShouldContain("Raleigh, NC");
        body.ShouldContain("Carla Captain");
        body.ShouldContain("Rita Roster");
        body.ShouldContain("Current user (You)");
        body.ShouldContain($"/Clubs/{club.ClubId}/admin");
        body.ShouldContain("Admin");
    }

    /// <summary>
    /// Verifies admin-page join-request UI content and retired route not-found behavior.
    /// </summary>
    [Fact]
    public async Task ClubAdminPage_ShowsPendingJoinRequestUi_AndRetiredRouteReturnsNotFoundAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAdminClient = fixture.CreateNovaHttpClient();
        using var joinerClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("club-admin-ui-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAdminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Parker", "Principal", clubId: null, cancellationToken);
        var club = await CreateClubAsync(clubAdminClient, "Albany Alliance", "Albany", "NY", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAdminClient, cancellationToken);

        var joinerEmail = UniqueEmail("club-admin-ui-joiner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(joinerClient, joinerEmail, Password, cancellationToken);
        await UpdateUserAsync(joinerEmail, "Jordan", "Joiner", clubId: null, cancellationToken);
        _ = await CreateJoinRequestAsync(joinerClient, club.ClubId, cancellationToken);

        using (var adminPageResponse = await clubAdminClient.GetAsync($"/Clubs/{club.ClubId}/admin", cancellationToken))
        {
            adminPageResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await adminPageResponse.Content.ReadAsStringAsync(cancellationToken);
            body.ShouldContain("Join Requests");
            body.ShouldContain("Jordan Joiner");
            body.ShouldContain("Approve");
            body.ShouldContain("Reject");
        }

        using var retiredRouteResponse = await clubAdminClient.GetAsync($"/Clubs/{club.ClubId}/admin/join-requests", cancellationToken);
        retiredRouteResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        retiredRouteResponse.Headers.Location.ShouldBeNull();
        retiredRouteResponse.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }

    /// <summary>
    /// Verifies approving and rejecting pending requests updates both admin and detail pages.
    /// </summary>
    [Fact]
    public async Task ClubAdminPage_ApproveRejectRoundTrip_UpdatesPendingListAndDetailRosterAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAdminClient = fixture.CreateNovaHttpClient();
        using var approvedJoinerClient = fixture.CreateNovaHttpClient();
        using var rejectedJoinerClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("club-roundtrip-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAdminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Robin", "Referee", clubId: null, cancellationToken);
        var club = await CreateClubAsync(clubAdminClient, "Orlando Orbit", "Orlando", "FL", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAdminClient, cancellationToken);

        var approvedEmail = UniqueEmail("club-roundtrip-approved");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(approvedJoinerClient, approvedEmail, Password, cancellationToken);
        await UpdateUserAsync(approvedEmail, "Paula", "PendingApprove", clubId: null, cancellationToken);
        var approvedRequest = await CreateJoinRequestAsync(approvedJoinerClient, club.ClubId, cancellationToken);

        var rejectedEmail = UniqueEmail("club-roundtrip-rejected");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(rejectedJoinerClient, rejectedEmail, Password, cancellationToken);
        await UpdateUserAsync(rejectedEmail, "Rex", "PendingReject", clubId: null, cancellationToken);
        var rejectedRequest = await CreateJoinRequestAsync(rejectedJoinerClient, club.ClubId, cancellationToken);

        using (var pendingBefore = await clubAdminClient.GetAsync($"/Clubs/{club.ClubId}/admin", cancellationToken))
        {
            pendingBefore.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await pendingBefore.Content.ReadAsStringAsync(cancellationToken);
            body.ShouldContain("Paula PendingApprove");
            body.ShouldContain("Rex PendingReject");
            body.ShouldContain("Approve");
            body.ShouldContain("Reject");
        }

        using (var approve = await clubAdminClient.PostAsync(ClubEndpoints.ApproveJoinRequestUrl(approvedRequest.ClubJoinRequestId), content: null, cancellationToken))
        {
            approve.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var reject = await clubAdminClient.PostAsync(ClubEndpoints.RejectJoinRequestUrl(rejectedRequest.ClubJoinRequestId), content: null, cancellationToken))
        {
            reject.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var pendingAfter = await clubAdminClient.GetAsync($"/Clubs/{club.ClubId}/admin", cancellationToken))
        {
            pendingAfter.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await pendingAfter.Content.ReadAsStringAsync(cancellationToken);
            // The pending list is cleared, and the newly approved member now appears in the
            // admin page's Members & Admins roster; the rejected user does not become a member.
            body.ShouldContain("No pending requests.");
            body.ShouldContain("Paula PendingApprove");
            body.ShouldNotContain("Rex PendingReject");
        }

        await RefreshClubMembershipCookieAsync(approvedJoinerClient, cancellationToken);

        using (var approvedDetail = await approvedJoinerClient.GetAsync($"/Clubs/{club.ClubId}", cancellationToken))
        {
            approvedDetail.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await approvedDetail.Content.ReadAsStringAsync(cancellationToken);
            body.ShouldContain("Paula PendingApprove");
            body.ShouldNotContain("Rex PendingReject");
        }

        using (var adminDetail = await clubAdminClient.GetAsync($"/Clubs/{club.ClubId}", cancellationToken))
        {
            adminDetail.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await adminDetail.Content.ReadAsStringAsync(cancellationToken);
            body.ShouldContain("Paula PendingApprove");
            body.ShouldNotContain("Rex PendingReject");
        }
    }

    /// <summary>
    /// Verifies the admin join-requests API route still behaves as a JSON API endpoint.
    /// </summary>
    [Fact]
    public async Task AdminJoinRequestsApi_RemainsJsonEndpointAfterUiRouteRemovalAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAdminClient = fixture.CreateNovaHttpClient();
        using var joinerClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("club-api-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAdminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Anya", "ApiAdmin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(clubAdminClient, "Tampa Tide", "Tampa", "FL", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAdminClient, cancellationToken);

        var joinerEmail = UniqueEmail("club-api-joiner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(joinerClient, joinerEmail, Password, cancellationToken);
        await UpdateUserAsync(joinerEmail, "Jamie", "JsonJoiner", clubId: null, cancellationToken);
        var request = await CreateJoinRequestAsync(joinerClient, club.ClubId, cancellationToken);

        using var response = await clubAdminClient.GetAsync(ClubEndpoints.AdminJoinRequestsUrl(club.ClubId), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var payload = await response.Content.ReadFromJsonAsync<IReadOnlyList<ClubJoinRequestDto>>(cancellationToken);
        payload.ShouldNotBeNull();
        payload.Count.ShouldBe(1);
        payload[0].ClubJoinRequestId.ShouldBe(request.ClubJoinRequestId);
        payload[0].RequestingUserName.ShouldBe("Jamie JsonJoiner");
    }

    /// <summary>
    /// Verifies the player-roster API route is registered and enforces expected auth boundaries.
    /// </summary>
    [Fact]
    public async Task PlayerRosterApi_Enforces401And403_AndAllowsSameClubMembersAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();
        using var clubAdminClient = fixture.CreateNovaHttpClient();
        using var sameClubMemberClient = fixture.CreateNovaHttpClient();
        using var otherClubMemberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("roster-api-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAdminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Rhea", "RosterAdmin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(clubAdminClient, "Nashville North", "Nashville", "TN", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAdminClient, cancellationToken);

        var memberEmail = UniqueEmail("roster-api-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(sameClubMemberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, "Mila", "Member", club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(sameClubMemberClient, cancellationToken);

        var otherClubEmail = UniqueEmail("roster-api-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClubMemberClient, otherClubEmail, Password, cancellationToken);
        await UpdateUserAsync(otherClubEmail, "Oscar", "OtherClub", clubId: null, cancellationToken);
        _ = await CreateClubAsync(otherClubMemberClient, "Memphis Monarchs", "Memphis", "TN", cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClubMemberClient, cancellationToken);

        var rosterUrl = GetPlayerRosterEndpoints.GetRosterUrl(club.ClubId);

        using (var anonymousResponse = await anonymousClient.GetAsync(rosterUrl, cancellationToken))
        {
            anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using (var crossClubResponse = await otherClubMemberClient.GetAsync(rosterUrl, cancellationToken))
        {
            crossClubResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            var problem = await crossClubResponse.ToServiceProblemAsync(cancellationToken);
            problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        }

        using (var sameClubResponse = await sameClubMemberClient.GetAsync(rosterUrl, cancellationToken))
        {
            sameClubResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var payload = await sameClubResponse.Content.ReadFromJsonAsync<PagedResult<PlayerListItem>>(cancellationToken);
            payload.ShouldNotBeNull();
            payload.Page.ShouldBe(1);
            payload.PageSize.ShouldBe(20);
        }
    }

    /// <summary>
    /// Creates a club over the HTTP API for the currently-authenticated client.
    /// </summary>
    /// <param name="client">The authenticated client creating the club.</param>
    /// <param name="name">The club name.</param>
    /// <param name="city">The club city.</param>
    /// <param name="state">The club state.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club payload.</returns>
    private static async Task<ClubDto> CreateClubAsync(
        HttpClient client,
        string name,
        string city,
        string state,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(ClubEndpoints.Create, new CreateClubInput
        {
            Name = name,
            City = city,
            State = state
        }, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();
        return club;
    }

    /// <summary>
    /// Creates a join request for the specified club using the authenticated client.
    /// </summary>
    /// <param name="client">The authenticated client submitting the request.</param>
    /// <param name="clubId">The target club id.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created join request payload.</returns>
    private static async Task<ClubJoinRequestDto> CreateJoinRequestAsync(
        HttpClient client,
        long clubId,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(ClubEndpoints.CreateJoinRequestUrl(clubId), content: null, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var request = await response.Content.ReadFromJsonAsync<ClubJoinRequestDto>(cancellationToken);
        request.ShouldNotBeNull();
        return request;
    }

    /// <summary>
    /// Calls the club onboarding completion endpoint so refreshed claims are baked into the auth cookie.
    /// </summary>
    /// <param name="client">The authenticated client.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes once the refresh hop returns.</returns>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>
    /// Updates seeded user profile fields (and optional club membership) through the admin context.
    /// </summary>
    /// <param name="email">The user email to update.</param>
    /// <param name="firstName">The first name to set.</param>
    /// <param name="lastName">The last name to set.</param>
    /// <param name="clubId">The optional club id assignment.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes once persisted.</returns>
    private async Task UpdateUserAsync(
        string email,
        string firstName,
        string lastName,
        long? clubId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var normalizedEmail = email.ToUpperInvariant();
        var user = await context.Users.SingleAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.ClubId = clubId;
        context.Users.Update(user);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Builds a unique email for each test user.
    /// </summary>
    /// <param name="prefix">A scenario prefix that improves traceability in failures.</param>
    /// <returns>A unique email address.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";
}
