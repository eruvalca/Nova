using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Dashboard;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Verifies dashboard summary and activity authorization, role-aware shaping, validation, and
/// serialization over HTTP against the Aspire-hosted application.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class DashboardHttpTests(NovaAppHostFixture fixture)
{
    /// <summary>Provides the password used by registered integration-test users.</summary>
    private const string Password = "Test#Passw0rd!";

    /// <summary>Verifies anonymous callers receive 401 for both dashboard routes.</summary>
    [Fact]
    public async Task GetEndpoints_RejectAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = fixture.CreateNovaHttpClient();

        using (var summary = await anonymous.GetAsync(DashboardEndpoints.GetSummary, cancellationToken))
        {
            summary.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using (var activity = await anonymous.GetAsync(DashboardEndpoints.GetActivity, cancellationToken))
        {
            activity.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var attention = await anonymous.GetAsync(DashboardEndpoints.GetAttention, cancellationToken);
        attention.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Verifies authenticated callers without a club receive 403 for both dashboard routes.</summary>
    [Fact]
    public async Task GetEndpoints_ReturnForbidden_ForAuthenticatedUserWithoutClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("dashboard-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, clubId: null, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);

        using (var summary = await client.GetAsync(DashboardEndpoints.GetSummary, cancellationToken))
        {
            summary.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var activity = await client.GetAsync(DashboardEndpoints.GetActivity, cancellationToken))
        {
            activity.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using var attention = await client.GetAsync(DashboardEndpoints.GetAttention, cancellationToken);
        attention.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies an administrator sees attention counts while an evaluator's summary omits them.
    /// </summary>
    [Fact]
    public async Task GetSummary_AdminSeesAttention_EvaluatorOmits()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("dashboard-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = SeedingHelpers.UniqueEmail("dashboard-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, memberEmail, club.ClubId, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        await using (var context = fixture.CreateAdminContext())
        {
            var adminUserId = await context.Users
                .Where(user => user.NormalizedEmail == adminEmail.ToUpperInvariant())
                .Select(user => user.Id)
                .SingleAsync(cancellationToken);
            var memberUserId = await context.Users
                .Where(user => user.NormalizedEmail == memberEmail.ToUpperInvariant())
                .Select(user => user.Id)
                .SingleAsync(cancellationToken);

            var season = new SeasonEntity { Name = "S", StartDate = new DateOnly(2026, 1, 1), ClubId = club.ClubId, CreatedById = adminUserId };
            var campaign = new CampaignEntity { Name = "C", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = 0, ClubId = club.ClubId, CreatedById = adminUserId };
            var player = new PlayerEntity { FirstName = "P", LastName = "A", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = club.ClubId, CreatedById = adminUserId };
            context.AddRange(season, campaign, player);
            await context.SaveChangesAsync(cancellationToken);

            context.AddRange(
                new PlayerCampaignAssignmentEntity { PlayerId = player.PlayerId, CampaignId = campaign.CampaignId, ClubId = club.ClubId, CreatedById = adminUserId, PlacementOutcome = PlacementOutcome.Undecided },
                new ClubJoinRequestEntity { ClubId = club.ClubId, RequestingUserId = memberUserId, CreatedById = memberUserId, Status = RequestStatus.Pending });
            await context.SaveChangesAsync(cancellationToken);
        }

        using (var adminResponse = await adminClient.GetAsync(DashboardEndpoints.GetSummary, cancellationToken))
        {
            adminResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var dashboard = await adminResponse.Content.ReadFromJsonAsync<ClubDashboardResult>(cancellationToken);
            dashboard.ShouldNotBeNull();
            dashboard.AdminAttention.ShouldNotBeNull();
            dashboard.AdminAttention!.PendingJoinRequestCount.ShouldBe(1);
            dashboard.AdminAttention.UnresolvedPlacementCount.ShouldBe(1);
            dashboard.AdminAttention.FirstUnresolvedCampaignId.ShouldNotBeNull();
        }

        using (var attentionResponse = await adminClient.GetAsync(DashboardEndpoints.GetAttention, cancellationToken))
        {
            attentionResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var attention = await attentionResponse.Content.ReadFromJsonAsync<AdminAttentionResult>(cancellationToken);
            attention.ShouldNotBeNull();
            attention.PendingJoinRequests.State.ShouldBe(AttentionProjectionState.Available);
            attention.PendingJoinRequests.Count.ShouldBe(1);
            attention.PendingJoinRequests.OldestSubmittedAt.ShouldNotBeNull();
            attention.NeedsPlacement.State.ShouldBe(AttentionProjectionState.Available);
            attention.NeedsPlacement.Count.ShouldBe(1);
            attention.NeedsPlacement.CampaignId.ShouldNotBeNull();
        }

        using (var memberResponse = await memberClient.GetAsync(DashboardEndpoints.GetSummary, cancellationToken))
        {
            memberResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var dashboard = await memberResponse.Content.ReadFromJsonAsync<ClubDashboardResult>(cancellationToken);
            dashboard.ShouldNotBeNull();
            dashboard.AdminAttention.ShouldBeNull();
        }


        using var memberAttention = await memberClient.GetAsync(DashboardEndpoints.GetAttention, cancellationToken);
        memberAttention.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>Verifies the activity endpoint serializes a successful, bounded result.</summary>
    [Fact]
    public async Task GetActivity_ReturnsSuccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = fixture.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("dashboard-activity");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, clubId: null, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(client, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);

        using (var response = await client.GetAsync(DashboardEndpoints.GetActivity, cancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var activity = await response.Content.ReadFromJsonAsync<DashboardActivityResult>(cancellationToken);
            activity.ShouldNotBeNull();
            activity.Events.ShouldNotBeNull();
        }
    }

    /// <summary>Verifies an invalid cursor produces correlated validation ProblemDetails.</summary>
    [Fact]
    public async Task GetActivity_InvalidCursor_ReturnsValidationProblem_WithTraceId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var client = fixture.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("dashboard-bad-cursor");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, clubId: null, cancellationToken);
        _ = await SeedingHelpers.CreateClubAsync(client, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(
            DashboardEndpoints.GetActivityUrl("not-base64"),
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        document.ShouldNotBeNull();
        document.RootElement.TryGetProperty("traceId", out _).ShouldBeTrue();
    }
}
