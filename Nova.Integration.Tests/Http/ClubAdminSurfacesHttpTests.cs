using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Account;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the club-administration mutation boundaries: join-request
/// approve/reject, member lifecycle, cancel-join-request, and the admin join-requests listing.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class ClubAdminSurfacesHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    // ── Join-request approve/reject ─────────────────────────────────────────────

    /// <summary>
    /// Verifies a non-admin club member cannot approve a join request.
    /// </summary>
    [Fact]
    public async Task Approve_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        using var joinerClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "approve-member-admin", "Member Approve Club", cancellationToken);
        await RegisterUserAsync(memberClient, "approve-member", "Member", "Approver", admin.Club.ClubId, cancellationToken);
        _ = await RegisterUserAsync(joinerClient, "approve-joiner", "Joiner", "Approver", clubId: null, cancellationToken);
        var request = await CreateJoinRequestAsync(joinerClient, admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.PostAsync(
            ClubEndpoints.ApproveJoinRequestUrl(request.ClubJoinRequestId), content: null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a non-admin club member cannot reject a join request.
    /// </summary>
    [Fact]
    public async Task Reject_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        using var joinerClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "reject-member-admin", "Member Reject Club", cancellationToken);
        await RegisterUserAsync(memberClient, "reject-member", "Member", "Rejecter", admin.Club.ClubId, cancellationToken);
        _ = await RegisterUserAsync(joinerClient, "reject-joiner", "Joiner", "Rejecter", clubId: null, cancellationToken);
        var request = await CreateJoinRequestAsync(joinerClient, admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.PostAsync(
            ClubEndpoints.RejectJoinRequestUrl(request.ClubJoinRequestId), content: null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies approving another club's join request is non-disclosing (404) and leaves it pending.
    /// </summary>
    [Fact]
    public async Task Approve_ReturnsNotFound_ForCrossTenantRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();
        using var joinerClient = fixture.CreateNovaHttpClient();

        var clubA = await RegisterClubAdminAsync(clubAClient, "approve-xclub-a", "Cross Approve A", cancellationToken);
        _ = await RegisterUserAsync(joinerClient, "approve-xclub-joiner", "Joiner", "Cross", clubId: null, cancellationToken);
        var request = await CreateJoinRequestAsync(joinerClient, clubA.Club.ClubId, cancellationToken);

        _ = await RegisterClubAdminAsync(clubBClient, "approve-xclub-b", "Cross Approve B", cancellationToken);

        using var approve = await clubBClient.PostAsync(
            ClubEndpoints.ApproveJoinRequestUrl(request.ClubJoinRequestId), content: null, cancellationToken);
        approve.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await approve.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        await using var db = fixture.CreateAdminContext();
        var status = await db.ClubJoinRequests
            .Where(candidate => candidate.ClubJoinRequestId == request.ClubJoinRequestId)
            .Select(candidate => candidate.Status)
            .SingleAsync(cancellationToken);
        status.ShouldBe(RequestStatus.Pending);
    }

    /// <summary>
    /// Verifies rejecting another club's join request is non-disclosing (404) and leaves it pending.
    /// </summary>
    [Fact]
    public async Task Reject_ReturnsNotFound_ForCrossTenantRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();
        using var joinerClient = fixture.CreateNovaHttpClient();

        var clubA = await RegisterClubAdminAsync(clubAClient, "reject-xclub-a", "Cross Reject A", cancellationToken);
        _ = await RegisterUserAsync(joinerClient, "reject-xclub-joiner", "Joiner", "Cross", clubId: null, cancellationToken);
        var request = await CreateJoinRequestAsync(joinerClient, clubA.Club.ClubId, cancellationToken);

        _ = await RegisterClubAdminAsync(clubBClient, "reject-xclub-b", "Cross Reject B", cancellationToken);

        using var reject = await clubBClient.PostAsync(
            ClubEndpoints.RejectJoinRequestUrl(request.ClubJoinRequestId), content: null, cancellationToken);
        reject.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await reject.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        await using var db = fixture.CreateAdminContext();
        var status = await db.ClubJoinRequests
            .Where(candidate => candidate.ClubJoinRequestId == request.ClubJoinRequestId)
            .Select(candidate => candidate.Status)
            .SingleAsync(cancellationToken);
        status.ShouldBe(RequestStatus.Pending);
    }

    // ── Assign ClubAdmin ────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies assign-admin rejects anonymous callers.
    /// </summary>
    [Fact]
    public async Task AssignClubAdmin_ReturnsUnauthorized_ForAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.PostAsync(ClubEndpoints.PromoteMemberUrl(1), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies a non-admin club member cannot assign the ClubAdmin role.
    /// </summary>
    [Fact]
    public async Task AssignClubAdmin_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "assign-member-admin", "Member Assign Club", cancellationToken);
        await RegisterUserAsync(memberClient, "assign-member", "Member", "Assigner", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.PostAsync(ClubEndpoints.PromoteMemberUrl(admin.UserId), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies assigning ClubAdmin to a member of another club is non-disclosing (404).
    /// </summary>
    [Fact]
    public async Task AssignClubAdmin_ReturnsNotFound_ForCrossClubTarget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();

        _ = await RegisterClubAdminAsync(clubAClient, "assign-xclub-a", "Cross Assign A", cancellationToken);
        var clubB = await RegisterClubAdminAsync(clubBClient, "assign-xclub-b", "Cross Assign B", cancellationToken);

        using var response = await clubAClient.PostAsync(ClubEndpoints.PromoteMemberUrl(clubB.UserId), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies a club administrator can promote a same-club member and receives true.
    /// </summary>
    [Fact]
    public async Task AssignClubAdmin_ReturnsOk_ForSameClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "assign-ok-admin", "Ok Assign Club", cancellationToken);
        var member = await RegisterUserAsync(memberClient, "assign-ok-member", "Member", "Promotee", admin.Club.ClubId, cancellationToken);

        using var response = await adminClient.PostAsync(ClubEndpoints.PromoteMemberUrl(member.UserId), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveMember_ReturnsNoContentAndClearsMembership_ForClubAdministrator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "remove-admin", "Remove Member", cancellationToken);
        var member = await RegisterUserAsync(memberClient, "remove-member", "Removed", "Member", admin.Club.ClubId, cancellationToken);

        using var response = await adminClient.DeleteAsync(ClubEndpoints.RemoveMemberUrl(member.UserId), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = fixture.CreateAdminContext();
        (await db.Users.SingleAsync(user => user.Id == member.UserId, cancellationToken)).ClubId.ShouldBeNull();
    }

    [Fact]
    public async Task RemoveMember_ReturnsForbidden_ForRegularMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "remove-forbid-admin", "Remove Forbidden", cancellationToken);
        var member = await RegisterUserAsync(memberClient, "remove-forbid-member", "Regular", "Member", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.DeleteAsync(ClubEndpoints.RemoveMemberUrl(admin.UserId), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LeaveClub_ReturnsNoContentAndClearsMembership_ForRegularMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "leave-admin", "Leave Club", cancellationToken);
        var member = await RegisterUserAsync(memberClient, "leave-member", "Leaving", "Member", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.DeleteAsync(ClubEndpoints.LeaveClub, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = fixture.CreateAdminContext();
        (await db.Users.SingleAsync(user => user.Id == member.UserId, cancellationToken)).ClubId.ShouldBeNull();
    }

    [Fact]
    public async Task LeaveClub_ReturnsUnauthorized_ForAnonymousCaller()
    {
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.DeleteAsync(ClubEndpoints.LeaveClub, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LeaveClub_ReturnsForbidden_ForUserWithoutClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await RegisterUserAsync(client, "leave-no-club", "No", "Club", clubId: null, cancellationToken);

        using var response = await client.DeleteAsync(ClubEndpoints.LeaveClub, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LeaveClub_ReturnsConflict_ForFinalMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(client, "leave-final", "Final Member", cancellationToken);

        using var response = await client.DeleteAsync(ClubEndpoints.LeaveClub, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Detail.ShouldBe("The final club member cannot leave. Delete the club instead.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task MemberMutationRoutes_ReturnBadRequest_ForInvalidMemberUserId(long memberUserId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(client, "member-id-validation", "Member Id Validation", cancellationToken);
        var urls = new[]
        {
            ClubEndpoints.PromoteMemberUrl(memberUserId),
            ClubEndpoints.DemoteMemberUrl(memberUserId),
            ClubEndpoints.RemoveMemberUrl(memberUserId),
        };

        using var promoteResponse = await client.PostAsync(urls[0], content: null, cancellationToken);
        using var demoteResponse = await client.PostAsync(urls[1], content: null, cancellationToken);
        using var removeResponse = await client.DeleteAsync(urls[2], cancellationToken);

        promoteResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        demoteResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        removeResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ── Cancel join request ─────────────────────────────────────────────────────

    /// <summary>
    /// Verifies a user can cancel their own pending join request (204).
    /// </summary>
    [Fact]
    public async Task CancelJoinRequest_ReturnsNoContent_ForOwnRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var joinerClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "cancel-own-admin", "Cancel Own Club", cancellationToken);
        _ = await RegisterUserAsync(joinerClient, "cancel-own-joiner", "Joiner", "Cancel", clubId: null, cancellationToken);
        var request = await CreateJoinRequestAsync(joinerClient, admin.Club.ClubId, cancellationToken);

        using var response = await joinerClient.DeleteAsync(ClubEndpoints.CancelJoinRequestUrl(request.ClubJoinRequestId), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Verifies cancel-join-request rejects anonymous callers.
    /// </summary>
    [Fact]
    public async Task CancelJoinRequest_ReturnsUnauthorized_ForAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.DeleteAsync(ClubEndpoints.CancelJoinRequestUrl(1), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies cancelling an unknown join request id is non-disclosing (404).
    /// </summary>
    [Fact]
    public async Task CancelJoinRequest_ReturnsNotFound_ForUnknownRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        _ = await RegisterUserAsync(client, "cancel-unknown", "Solo", "Canceller", clubId: null, cancellationToken);

        using var response = await client.DeleteAsync(
            ClubEndpoints.CancelJoinRequestUrl(long.MaxValue),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ── Admin join-requests listing ─────────────────────────────────────────────

    /// <summary>
    /// Verifies a non-admin club member cannot read the admin join-requests listing.
    /// </summary>
    [Fact]
    public async Task AdminJoinRequests_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "list-member-admin", "List Member Club", cancellationToken);
        await RegisterUserAsync(memberClient, "list-member", "Member", "Lister", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.GetAsync(ClubEndpoints.AdminJoinRequestsUrl(admin.Club.ClubId), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies concurrent demotions serialize and cannot leave a club without an administrator.
    /// </summary>
    [Fact]
    public async Task DemoteMember_ConcurrentAdministrators_PreservesOneAdministrator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var firstClient = fixture.CreateNovaHttpClient();
        using var secondClient = fixture.CreateNovaHttpClient();

        var first = await RegisterClubAdminAsync(firstClient, "demote-race-first", "Demote Race", cancellationToken);
        var second = await RegisterUserAsync(secondClient, "demote-race-second", "Second", "Admin", first.Club.ClubId, cancellationToken);
        using (var promotion = await firstClient.PostAsync(
                   ClubEndpoints.PromoteMemberUrl(second.UserId), null, cancellationToken))
        {
            promotion.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await SeedingHelpers.RefreshClubMembershipCookieAsync(secondClient, cancellationToken);
        var firstDemotion = firstClient.PostAsync(ClubEndpoints.DemoteMemberUrl(second.UserId), null, cancellationToken);
        var secondDemotion = secondClient.PostAsync(ClubEndpoints.DemoteMemberUrl(first.UserId), null, cancellationToken);
        using var firstResponse = await firstDemotion;
        using var secondResponse = await secondDemotion;

        new[] { firstResponse.StatusCode, secondResponse.StatusCode }
            .ShouldContain(HttpStatusCode.NoContent);
        new[] { firstResponse.StatusCode, secondResponse.StatusCode }
            .ShouldContain(HttpStatusCode.Forbidden);

        await using var db = fixture.CreateAdminContext();
        var administratorRoleId = await db.Roles
            .Where(role => role.NormalizedName == Nova.Shared.Security.Roles.ClubAdmin.ToUpperInvariant())
            .Select(role => role.Id)
            .SingleAsync(cancellationToken);
        var administratorCount = await (from user in db.Users
                                        join role in db.UserRoles on user.Id equals role.UserId
                                        where user.ClubId == first.Club.ClubId && role.RoleId == administratorRoleId
                                        select user.Id).CountAsync(cancellationToken);
        administratorCount.ShouldBe(1);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<(ClubDto Club, string Email, long UserId)> RegisterClubAdminAsync(
        HttpClient client,
        string emailPrefix,
        string clubName,
        CancellationToken cancellationToken)
    {
        var (email, userId) = await RegisterUserAsync(client, emailPrefix, "Club", "Admin", clubId: null, cancellationToken);

        using var response = await client.PostAsync(
            ClubEndpoints.Create,
            SeedingHelpers.CreateClubMultipartContent($"{clubName} {Guid.CreateVersion7():N}", "Austin", "TX"),
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();

        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
        return (club, email, userId);
    }

    private async Task<(string Email, long UserId)> RegisterUserAsync(
        HttpClient client,
        string emailPrefix,
        string firstName,
        string lastName,
        long? clubId,
        CancellationToken cancellationToken)
    {
        var email = SeedingHelpers.UniqueEmail(emailPrefix);
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);

        await using var context = fixture.CreateAdminContext();
        var normalizedEmail = email.ToUpperInvariant();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.ClubId = clubId;
        await context.SaveChangesAsync(cancellationToken);

        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
        return (email, user.Id);
    }

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
}
