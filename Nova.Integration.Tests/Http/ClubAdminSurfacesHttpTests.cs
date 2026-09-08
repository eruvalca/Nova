using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Account;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Results;
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
    public async Task ApproveReturnsForbiddenForClubMemberAsync()
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
new Uri(ClubEndpoints.ApproveJoinRequestUrl(request.ClubJoinRequestId), UriKind.RelativeOrAbsolute), content: null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a non-admin club member cannot reject a join request.
    /// </summary>
    [Fact]
    public async Task RejectReturnsForbiddenForClubMemberAsync()
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
new Uri(ClubEndpoints.RejectJoinRequestUrl(request.ClubJoinRequestId), UriKind.RelativeOrAbsolute), content: null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies approving another club's join request is non-disclosing (404) and leaves it pending.
    /// </summary>
    [Fact]
    public async Task ApproveReturnsNotFoundForCrossTenantRequestAsync()
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
new Uri(ClubEndpoints.ApproveJoinRequestUrl(request.ClubJoinRequestId), UriKind.RelativeOrAbsolute), content: null, cancellationToken);
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
    public async Task RejectReturnsNotFoundForCrossTenantRequestAsync()
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
new Uri(ClubEndpoints.RejectJoinRequestUrl(request.ClubJoinRequestId), UriKind.RelativeOrAbsolute), content: null, cancellationToken);
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
    public async Task AssignClubAdminReturnsUnauthorizedForAnonymousAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.PostAsync(new Uri(ClubEndpoints.PromoteMemberUrl(1), UriKind.RelativeOrAbsolute), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies a non-admin club member cannot assign the ClubAdmin role.
    /// </summary>
    [Fact]
    public async Task AssignClubAdminReturnsForbiddenForClubMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "assign-member-admin", "Member Assign Club", cancellationToken);
        await RegisterUserAsync(memberClient, "assign-member", "Member", "Assigner", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.PostAsync(new Uri(ClubEndpoints.PromoteMemberUrl(admin.UserId), UriKind.RelativeOrAbsolute), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies assigning ClubAdmin to a member of another club is non-disclosing (404).
    /// </summary>
    [Fact]
    public async Task AssignClubAdminReturnsNotFoundForCrossClubTargetAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();

        _ = await RegisterClubAdminAsync(clubAClient, "assign-xclub-a", "Cross Assign A", cancellationToken);
        var clubB = await RegisterClubAdminAsync(clubBClient, "assign-xclub-b", "Cross Assign B", cancellationToken);

        using var response = await clubAClient.PostAsync(new Uri(ClubEndpoints.PromoteMemberUrl(clubB.UserId), UriKind.RelativeOrAbsolute), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies a club administrator can promote a same-club member and receives true.
    /// </summary>
    [Fact]
    public async Task AssignClubAdminReturnsOkForSameClubMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "assign-ok-admin", "Ok Assign Club", cancellationToken);
        var member = await RegisterUserAsync(memberClient, "assign-ok-member", "Member", "Promotee", admin.Club.ClubId, cancellationToken);

        using var response = await adminClient.PostAsync(new Uri(ClubEndpoints.PromoteMemberUrl(member.UserId), UriKind.RelativeOrAbsolute), null, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RemoveMemberReturnsNoContentAndClearsMembershipForClubAdministratorAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "remove-admin", "Remove Member", cancellationToken);
        var member = await RegisterUserAsync(memberClient, "remove-member", "Removed", "Member", admin.Club.ClubId, cancellationToken);

        using var response = await adminClient.DeleteAsync(new Uri(ClubEndpoints.RemoveMemberUrl(member.UserId), UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var db = fixture.CreateAdminContext();
        (await db.Users.SingleAsync(user => user.Id == member.UserId, cancellationToken)).ClubId.ShouldBeNull();
    }

    [Fact]
    public async Task RemoveMemberReturnsForbiddenForRegularMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "remove-forbid-admin", "Remove Forbidden", cancellationToken);
        _ = await RegisterUserAsync(memberClient, "remove-forbid-member", "Regular", "Member", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.DeleteAsync(new Uri(ClubEndpoints.RemoveMemberUrl(admin.UserId), UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Verifies administrator middleware rejects a regular member on the demotion route.</summary>
    [Fact]
    public async Task DemoteMemberReturnsForbiddenForRegularMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "demote-forbid-admin", "Demote Forbidden", cancellationToken);
        _ = await RegisterUserAsync(memberClient, "demote-forbid-member", "Regular", "Member", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.PostAsync(
new Uri(ClubEndpoints.DemoteMemberUrl(admin.UserId), UriKind.RelativeOrAbsolute),
            content: null,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LeaveClubReturnsNoContentAndClearsMembershipForRegularMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "leave-admin", "Leave Club", cancellationToken);
        var member = await RegisterUserAsync(memberClient, "leave-member", "Leaving", "Member", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.DeleteAsync(new Uri(ClubEndpoints.LeaveClub, UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var memberRouteResponse = await memberClient.GetAsync(new Uri(ClubEndpoints.GetMembers, UriKind.RelativeOrAbsolute), cancellationToken);
        memberRouteResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await using var db = fixture.CreateAdminContext();
        (await db.Users.SingleAsync(user => user.Id == member.UserId, cancellationToken)).ClubId.ShouldBeNull();
    }

    [Fact]
    public async Task LeaveClubReturnsUnauthorizedForAnonymousCallerAsync()
    {
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.DeleteAsync(new Uri(ClubEndpoints.LeaveClub, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LeaveClubReturnsForbiddenForUserWithoutClubAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await RegisterUserAsync(client, "leave-no-club", "No", "Club", clubId: null, cancellationToken);

        using var response = await client.DeleteAsync(new Uri(ClubEndpoints.LeaveClub, UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LeaveClubReturnsConflictForFinalMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(client, "leave-final", "Final Member", cancellationToken);

        using var response = await client.DeleteAsync(new Uri(ClubEndpoints.LeaveClub, UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Detail.ShouldBe("The final club member cannot leave. Delete the club instead.");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task MemberMutationRoutesReturnBadRequestForInvalidMemberUserIdAsync(long memberUserId)
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

        using var promoteResponse = await client.PostAsync(new Uri(urls[0], UriKind.RelativeOrAbsolute), content: null, cancellationToken);
        using var demoteResponse = await client.PostAsync(new Uri(urls[1], UriKind.RelativeOrAbsolute), content: null, cancellationToken);
        using var removeResponse = await client.DeleteAsync(new Uri(urls[2], UriKind.RelativeOrAbsolute), cancellationToken);

        promoteResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        demoteResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        removeResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ── Cancel join request ─────────────────────────────────────────────────────

    /// <summary>
    /// Verifies a user can cancel their own pending join request (204).
    /// </summary>
    [Fact]
    public async Task CancelJoinRequestReturnsNoContentForOwnRequestAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var joinerClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "cancel-own-admin", "Cancel Own Club", cancellationToken);
        _ = await RegisterUserAsync(joinerClient, "cancel-own-joiner", "Joiner", "Cancel", clubId: null, cancellationToken);
        var request = await CreateJoinRequestAsync(joinerClient, admin.Club.ClubId, cancellationToken);

        using var response = await joinerClient.DeleteAsync(new Uri(ClubEndpoints.CancelJoinRequestUrl(request.ClubJoinRequestId), UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Verifies cancel-join-request rejects anonymous callers.
    /// </summary>
    [Fact]
    public async Task CancelJoinRequestReturnsUnauthorizedForAnonymousAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.DeleteAsync(new Uri(ClubEndpoints.CancelJoinRequestUrl(1), UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies cancelling an unknown join request id is non-disclosing (404).
    /// </summary>
    [Fact]
    public async Task CancelJoinRequestReturnsNotFoundForUnknownRequestAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        _ = await RegisterUserAsync(client, "cancel-unknown", "Solo", "Canceller", clubId: null, cancellationToken);

        using var response = await client.DeleteAsync(
new Uri(ClubEndpoints.CancelJoinRequestUrl(long.MaxValue), UriKind.RelativeOrAbsolute),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ── Admin join-requests listing ─────────────────────────────────────────────

    /// <summary>
    /// Verifies a non-admin club member cannot read the admin join-requests listing.
    /// </summary>
    [Fact]
    public async Task AdminJoinRequestsReturnsForbiddenForClubMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "list-member-admin", "List Member Club", cancellationToken);
        await RegisterUserAsync(memberClient, "list-member", "Member", "Lister", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.GetAsync(new Uri(ClubEndpoints.AdminJoinRequestsUrl(admin.Club.ClubId), UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies concurrent demotions serialize and cannot leave a club without an administrator.
    /// </summary>
    [Fact]
    public async Task DemoteMemberConcurrentAdministratorsPreservesOneAdministratorAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var firstClient = fixture.CreateNovaHttpClient();
        using var secondClient = fixture.CreateNovaHttpClient();

        var first = await RegisterClubAdminAsync(firstClient, "demote-race-first", "Demote Race", cancellationToken);
        var second = await RegisterUserAsync(secondClient, "demote-race-second", "Second", "Admin", first.Club.ClubId, cancellationToken);
        using (var promotion = await firstClient.PostAsync(
new Uri(ClubEndpoints.PromoteMemberUrl(second.UserId), UriKind.RelativeOrAbsolute), null, cancellationToken))
        {
            promotion.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await SeedingHelpers.RefreshClubMembershipCookieAsync(secondClient, cancellationToken);

        await using var lockDb = fixture.CreateAdminContext();
        await using var lockTransaction = await lockDb.Database.BeginTransactionAsync(cancellationToken);
        foreach (var userId in new[] { first.UserId, second.UserId }.Order())
        {
            var userLockKey = (long.MinValue / 64) + userId;
            await lockDb.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({userLockKey})",
                cancellationToken);
        }

        var clubLockKey = (long.MinValue / 32) + first.Club.ClubId;
        await lockDb.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({clubLockKey})",
            cancellationToken);
        var lockHolderBackendPid = await lockDb.Database
            .SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(cancellationToken);

        var firstDemotion = firstClient.PostAsync(new Uri(ClubEndpoints.DemoteMemberUrl(second.UserId), UriKind.RelativeOrAbsolute), null, cancellationToken);
        var secondDemotion = secondClient.PostAsync(new Uri(ClubEndpoints.DemoteMemberUrl(first.UserId), UriKind.RelativeOrAbsolute), null, cancellationToken);
        await WaitForBlockedRequestsAsync(lockHolderBackendPid, expectedCount: 2, cancellationToken);
        await lockTransaction.CommitAsync(cancellationToken);
        using var firstResponse = await firstDemotion;
        using var secondResponse = await secondDemotion;

        new[] { firstResponse.StatusCode, secondResponse.StatusCode }
            .ShouldContain(HttpStatusCode.NoContent);
        new[] { firstResponse.StatusCode, secondResponse.StatusCode }
            .ShouldContain(HttpStatusCode.Forbidden);

        await using var db = fixture.CreateAdminContext();
        var administratorRoleId = await db.Roles
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            .Where(role => role.NormalizedName == Nova.SharedKernel.Security.Roles.ClubAdmin.ToUpperInvariant())
#pragma warning restore CA1862
            .Select(role => role.Id)
            .SingleAsync(cancellationToken);
        var administratorCount = await (from user in db.Users
                                        join role in db.UserRoles on user.Id equals role.UserId
                                        where user.ClubId == first.Club.ClubId && role.RoleId == administratorRoleId
                                        select user.Id).CountAsync(cancellationToken);
        administratorCount.ShouldBe(1);
    }

    /// <summary>Waits until the expected number of HTTP mutations are blocked behind one lock holder.</summary>
    /// <param name="lockHolderBackendPid">The PostgreSQL backend process holding the membership locks.</param>
    /// <param name="expectedCount">The number of competing requests that must be waiting.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task that completes when every competing request is observably blocked.</returns>
    private async Task WaitForBlockedRequestsAsync(
        int lockHolderBackendPid,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var observer = fixture.CreateAdminContext();
            await using (observer)
            {
                var blockedCount = await observer.Database.SqlQuery<int>(
                $"""SELECT count(*)::integer AS "Value" FROM pg_stat_activity WHERE {lockHolderBackendPid} = ANY(pg_blocking_pids(pid))""")
                .SingleAsync(cancellationToken);
                if (blockedCount >= expectedCount)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }

        throw new TimeoutException($"Expected {expectedCount} membership mutations to block behind PostgreSQL backend {lockHolderBackendPid}.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<(ClubDto Club, string Email, long UserId)> RegisterClubAdminAsync(
        HttpClient client,
        string emailPrefix,
        string clubName,
        CancellationToken cancellationToken)
    {
        var (email, userId) = await RegisterUserAsync(client, emailPrefix, "Club", "Admin", clubId: null, cancellationToken);

        using var responseRequestContent = SeedingHelpers.CreateClubMultipartContent($"{clubName} {Guid.CreateVersion7():N}", "Austin", "TX");
        using var response = await client.PostAsync(
        new Uri(ClubEndpoints.Create, UriKind.RelativeOrAbsolute),
                    responseRequestContent,
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

        var context = fixture.CreateAdminContext();
        await using (context)
        {
            var normalizedEmail = email.ToUpperInvariant();
            var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
            user.FirstName = firstName;
            user.LastName = lastName;
            user.ClubId = clubId;
            await context.SaveChangesAsync(cancellationToken);

            await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
            return (email, user.Id);
        }
    }

    private static async Task<ClubJoinRequestDto> CreateJoinRequestAsync(
        HttpClient client,
        long clubId,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(new Uri(ClubEndpoints.CreateJoinRequestUrl(clubId), UriKind.RelativeOrAbsolute), content: null, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var request = await response.Content.ReadFromJsonAsync<ClubJoinRequestDto>(cancellationToken);
        request.ShouldNotBeNull();
        return request;
    }
}
