using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the closeout-readiness and recent-activity endpoints, plus the
/// closed-history readability pass across every campaign read surface.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignCloseoutHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>Verifies anonymous callers receive unauthorized for both read routes.</summary>
    [Fact]
    public async Task ReadinessAndActivity_ReturnUnauthorized_ForAnonymousCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = fixture.CreateNovaHttpClient();

        using (var readiness = await anonymous.GetAsync(CampaignEndpoints.GetCampaignCloseoutReadinessUrl(1), cancellationToken))
        {
            readiness.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using (var activity = await anonymous.GetAsync(CampaignEndpoints.GetCampaignActivityUrl(new GetCampaignActivityInput { CampaignId = 1 }), cancellationToken))
        {
            activity.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    /// <summary>Verifies authenticated users without a club receive forbidden for both read routes.</summary>
    [Fact]
    public async Task ReadinessAndActivity_ReturnForbidden_ForAuthenticatedUserWithoutClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("closeout-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using (var readiness = await client.GetAsync(CampaignEndpoints.GetCampaignCloseoutReadinessUrl(1), cancellationToken))
        {
            readiness.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var activity = await client.GetAsync(CampaignEndpoints.GetCampaignActivityUrl(new GetCampaignActivityInput { CampaignId = 1 }), cancellationToken))
        {
            activity.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    /// <summary>Verifies a blocked campaign's readiness carries the seeded undecided assignment ids.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsBlockedReadiness_WithSeededAssignmentIds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("closeout-blocked-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var memberEmail = UniqueEmail("closeout-blocked-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        var seeded = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture,
            club.ClubId,
            adminEmail,
            "Closeout Blocked",
            participantCount: 3,
            PlacementOutcome.Undecided,
            cancellationToken);

        using var response = await memberClient.GetAsync(CampaignEndpoints.GetCampaignCloseoutReadinessUrl(seeded.CampaignId), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var readiness = await response.Content.ReadFromJsonAsync<CampaignCloseoutReadinessDto>(cancellationToken);
        readiness.ShouldNotBeNull();
        readiness.IsReady.ShouldBeFalse();
        readiness.Status.ShouldBe(CampaignStatus.Active);
        readiness.Summary.UndecidedCount.ShouldBe(3);
        readiness.Summary.TotalCount.ShouldBe(3);

        var blocker = readiness.Blockers.ShouldHaveSingleItem();
        blocker.Condition.ShouldBe(CloseoutBlockerConditions.Outcomes);
        blocker.Count.ShouldBe(3);
        blocker.AssignmentIds.OrderBy(id => id).ShouldBe(seeded.AssignmentIds.OrderBy(id => id));
    }

    /// <summary>Verifies another club's campaign readiness and activity return non-disclosing not-found.</summary>
    [Fact]
    public async Task ReadinessAndActivity_ReturnNotFound_ForCrossTenantCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("closeout-cross-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var clubA = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var memberEmail = UniqueEmail("closeout-cross-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, clubId: null, cancellationToken);
        var clubB = await CreateClubAsync(memberClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        var seeded = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture,
            clubA.ClubId,
            adminEmail,
            "Closeout Cross",
            participantCount: 1,
            PlacementOutcome.Undecided,
            cancellationToken);

        using (var readiness = await memberClient.GetAsync(CampaignEndpoints.GetCampaignCloseoutReadinessUrl(seeded.CampaignId), cancellationToken))
        {
            readiness.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            await AssertNoDetailAsync(readiness, cancellationToken);
        }

        using (var activity = await memberClient.GetAsync(CampaignEndpoints.GetCampaignActivityUrl(new GetCampaignActivityInput { CampaignId = seeded.CampaignId }), cancellationToken))
        {
            activity.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            await AssertNoDetailAsync(activity, cancellationToken);
        }
    }

    /// <summary>Verifies a Closed campaign is readable by an evaluator and an administrator through every read surface.</summary>
    /// <param name="isAdmin">Whether the viewer is the club administrator.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedCampaign_IsReadableByEvaluatorAndAdmin_AcrossReadSurfaces(bool isAdmin)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var viewerClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("closeout-closed-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken, firstName: "Admin", lastName: "A");
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var viewerEmail = UniqueEmail("closeout-closed-viewer");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(viewerClient, viewerEmail, Password, cancellationToken);
        await UpdateUserAsync(viewerEmail, club.ClubId, cancellationToken, firstName: "Casey", lastName: "Viewer");
        await RefreshClubMembershipCookieAsync(viewerClient, cancellationToken);

        var adminUserId = await GetUserIdByEmailAsync(adminEmail, cancellationToken);
        var seeded = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture,
            club.ClubId,
            adminEmail,
            "Closeout Closed",
            participantCount: 2,
            PlacementOutcome.NotSelected,
            cancellationToken);
        await SeedingHelpers.CloseCampaignThroughServiceAsync(fixture, club.ClubId, adminUserId, seeded.CampaignId, cancellationToken);

        using var viewer = isAdmin ? adminClient : viewerClient;

        // Closeout readiness.
        using (var readinessResponse = await viewer.GetAsync(CampaignEndpoints.GetCampaignCloseoutReadinessUrl(seeded.CampaignId), cancellationToken))
        {
            readinessResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var readiness = await readinessResponse.Content.ReadFromJsonAsync<CampaignCloseoutReadinessDto>(cancellationToken);
            readiness.ShouldNotBeNull();
            readiness.Status.ShouldBe(CampaignStatus.Closed);
            readiness.IsReady.ShouldBeTrue();
            readiness.Blockers.ShouldBeEmpty();
        }

        // Activity carries the closed transition.
        using (var activityResponse = await viewer.GetAsync(CampaignEndpoints.GetCampaignActivityUrl(new GetCampaignActivityInput { CampaignId = seeded.CampaignId }), cancellationToken))
        {
            activityResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var activity = await activityResponse.Content.ReadFromJsonAsync<CampaignActivityResult>(cancellationToken);
            activity.ShouldNotBeNull();
            activity.Events.ShouldNotBeEmpty();
            activity.Events.ShouldAllBe(item => item.EventType == CampaignLifecycleEventType.Closed);
        }

        // Detail carries closure fields.
        using (var detailResponse = await viewer.GetAsync(CampaignEndpoints.GetCampaignDetailUrl(seeded.CampaignId), cancellationToken))
        {
            detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var detail = await detailResponse.Content.ReadFromJsonAsync<CampaignDetailResult>(cancellationToken);
            detail.ShouldNotBeNull();
            detail.Status.ShouldBe(CampaignStatus.Closed);
            detail.ClosedAt.ShouldNotBeNull();
            detail.ClosedByUserId.ShouldBe(adminUserId);
        }

        // Placement roster and summary remain readable.
        using (var rosterResponse = await viewer.GetAsync(CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput { CampaignId = seeded.CampaignId }), cancellationToken))
        {
            rosterResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var summaryResponse = await viewer.GetAsync(CampaignEndpoints.GetCampaignPlacementSummaryUrl(seeded.CampaignId), cancellationToken))
        {
            summaryResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var summary = await summaryResponse.Content.ReadFromJsonAsync<CampaignPlacementSummaryDto>(cancellationToken);
            summary.ShouldNotBeNull();
            summary.NotSelectedCount.ShouldBe(2);
            summary.TotalCount.ShouldBe(2);
        }
    }

    /// <summary>Verifies the activity endpoint returns bounded, ordered close+reopen events.</summary>
    [Fact]
    public async Task GetActivity_ReturnsBoundedOrderedEvents_AfterCloseAndReopen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("closeout-activity-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken, firstName: "Admin", lastName: "A");
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var memberEmail = UniqueEmail("closeout-activity-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken, firstName: "Casey", lastName: "Viewer");
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        var adminUserId = await GetUserIdByEmailAsync(adminEmail, cancellationToken);
        var seeded = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture,
            club.ClubId,
            adminEmail,
            "Closeout Activity",
            participantCount: 1,
            PlacementOutcome.NotSelected,
            cancellationToken);
        await SeedingHelpers.CloseCampaignThroughServiceAsync(fixture, club.ClubId, adminUserId, seeded.CampaignId, cancellationToken);
        await SeedingHelpers.ReopenCampaignThroughServiceAsync(fixture, club.ClubId, adminUserId, seeded.CampaignId, cancellationToken);

        using var response = await memberClient.GetAsync(CampaignEndpoints.GetCampaignActivityUrl(new GetCampaignActivityInput { CampaignId = seeded.CampaignId }), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var activity = await response.Content.ReadFromJsonAsync<CampaignActivityResult>(cancellationToken);
        activity.ShouldNotBeNull();
        activity.Events.Count.ShouldBe(2);

        var ordered = activity.Events
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.CampaignLifecycleEventId)
            .Select(item => item.CampaignLifecycleEventId)
            .ToList();
        activity.Events.Select(item => item.CampaignLifecycleEventId).ShouldBe(ordered);
        activity.Events.Select(item => item.EventType).ShouldBe(
        [
            CampaignLifecycleEventType.Reopened,
            CampaignLifecycleEventType.Closed
        ]);
    }

    /// <summary>Creates a unique integration-test email address.</summary>
    /// <param name="prefix">The scenario prefix.</param>
    /// <returns>A unique email address.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    /// <summary>Creates a club through the public HTTP endpoint.</summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created club.</returns>
    private static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(ClubEndpoints.Create, SeedingHelpers.CreateClubMultipartContent($"Club {Guid.NewGuid():N}", "X", "TX"), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    /// <summary>Refreshes the authentication cookie after club membership changes.</summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the refresh operation.</returns>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/dashboard", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>Updates a registered user's club membership and optional display name directly for test setup.</summary>
    /// <param name="email">The registered email.</param>
    /// <param name="clubId">The optional club identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="firstName">The optional first name.</param>
    /// <param name="lastName">The optional last name.</param>
    /// <returns>A task representing the update.</returns>
    private async Task UpdateUserAsync(
        string email,
        long? clubId,
        CancellationToken cancellationToken,
        string? firstName = null,
        string? lastName = null)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        user.ClubId = clubId;
        if (firstName is not null)
        {
            user.FirstName = firstName;
        }

        if (lastName is not null)
        {
            user.LastName = lastName;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Gets the user identifier for the specified email.</summary>
    /// <param name="email">The user email.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The user identifier.</returns>
    private async Task<long> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        return await context.Users
            .Where(candidate => candidate.NormalizedEmail == email.ToUpperInvariant())
            .Select(candidate => candidate.Id)
            .SingleAsync(cancellationToken);
    }

    /// <summary>Asserts a not-found response carries no non-disclosing <c>detail</c> property.</summary>
    /// <param name="response">The problem-details response to inspect.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the body has been inspected.</returns>
    private static async Task AssertNoDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
    }
}
