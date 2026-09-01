using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Verifies the club activity feed endpoint over HTTP: keyset cursor paging, member-versus-admin
/// payload shaping, and admin-only visibility.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class ClubActivityHttpTests(NovaAppHostFixture fixture)
{
    /// <summary>Provides the password used by registered integration-test users.</summary>
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies the feed pages through a keyset continuation cursor over HTTP: page one returns 20
    /// events with a continuation, page two returns the remainder with no overlap.
    /// </summary>
    [Fact]
    public async Task GetActivity_PagesWithKeysetCursor_OverHttp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var (club, adminUserId) = await CreateClubWithAdminAsync(adminClient, cancellationToken);
        using var memberClient = await CreateMemberClientAsync(club.ClubId, cancellationToken);

        await SeedActivityEventsAsync(club.ClubId, adminUserId, count: 21, cancellationToken);

        using var firstResponse = await memberClient.GetAsync(ActivityEndpoints.GetClubActivity, cancellationToken);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<ClubActivityResult>(WebJsonOptions, cancellationToken);
        first.ShouldNotBeNull();
        first.Events.Count.ShouldBe(20);
        first.HasMore.ShouldBeTrue();
        first.NextCursor.ShouldNotBeNull();

        using var secondResponse = await memberClient.GetAsync(ActivityEndpoints.GetClubActivityUrl(first.NextCursor), cancellationToken);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await secondResponse.Content.ReadFromJsonAsync<ClubActivityResult>(WebJsonOptions, cancellationToken);
        second.ShouldNotBeNull();
        second.Events.Count.ShouldBe(1);
        second.HasMore.ShouldBeFalse();
        second.NextCursor.ShouldBeNull();

        var firstIds = first.Events.Select(item => item.ActivityEventId).ToHashSet();
        foreach (var item in second.Events)
        {
            firstIds.ShouldNotContain(item.ActivityEventId);
        }

        var all = first.Events.Concat(second.Events).ToList();
        var expectedOrder = all.OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.ActivityEventId).ToList();
        all.ShouldBe(expectedOrder);
    }

    /// <summary>
    /// Verifies a cursor whose occurrence time carries a non-zero UTC offset is normalized to UTC
    /// and served, rather than rejected by Npgsql's offset-zero-only timestamptz binding.
    /// </summary>
    [Fact]
    public async Task GetActivity_AcceptsNonZeroOffsetCursor_OverHttp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var (club, adminUserId) = await CreateClubWithAdminAsync(adminClient, cancellationToken);
        using var memberClient = await CreateMemberClientAsync(club.ClubId, cancellationToken);

        await SeedActivityEventsAsync(club.ClubId, adminUserId, count: 21, cancellationToken);

        using var firstResponse = await memberClient.GetAsync(ActivityEndpoints.GetClubActivity, cancellationToken);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<ClubActivityResult>(WebJsonOptions, cancellationToken);
        first.ShouldNotBeNull();
        var cursor = first.NextCursor.ShouldNotBeNull();

        // Replay the same instant with a non-zero offset: the comparison instant is unchanged, but
        // the textual offset is not accepted by Npgsql's timestamptz binding unless the service
        // normalizes it to UTC.
        var offsetOccurredAt = cursor.OccurredAt.ToOffset(TimeSpan.FromHours(2));
        var url = $"{ActivityEndpoints.GetClubActivity}?beforeActivityEventId={cursor.ActivityEventId}&beforeOccurredAt={Uri.EscapeDataString(offsetOccurredAt.ToString("O"))}";

        using var offsetResponse = await memberClient.GetAsync(url, cancellationToken);
        offsetResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await offsetResponse.Content.ReadFromJsonAsync<ClubActivityResult>(WebJsonOptions, cancellationToken);
        second.ShouldNotBeNull();
        second.Events.Count.ShouldBe(1);
        second.HasMore.ShouldBeFalse();
    }

    /// <summary>
    /// Verifies the feed shapes a MemberJoined payload by role over HTTP: a non-admin member sees
    /// no approving actor name while an administrator sees the stored approving actor name.
    /// </summary>
    [Fact]
    public async Task GetActivity_ShapesMemberJoinedByRole_OverHttp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var (club, adminUserId) = await CreateClubWithAdminAsync(adminClient, cancellationToken);
        using var memberClient = await CreateMemberClientAsync(club.ClubId, cancellationToken);

        var payload = JsonSerializer.Serialize(
            new MembershipContext { MemberDisplayName = "Jordan Lee", ApprovedByActorName = "Club Admin" },
            typeof(ClubActivityContext));
        await SeedActivityEventsAsync(club.ClubId, adminUserId, count: 1, cancellationToken, kind: ActivityEventKind.MemberJoined, payloadJson: payload);

        using var memberResponse = await memberClient.GetAsync(ActivityEndpoints.GetClubActivity, cancellationToken);
        memberResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var memberResult = await memberResponse.Content.ReadFromJsonAsync<ClubActivityResult>(WebJsonOptions, cancellationToken);
        memberResult.ShouldNotBeNull();
        var memberContext = memberResult.Events.Single().Context.ShouldBeOfType<MembershipContext>();
        memberContext.MemberDisplayName.ShouldBe("Jordan Lee");
        memberContext.ApprovedByActorName.ShouldBeNull();

        using var adminResponse = await adminClient.GetAsync(ActivityEndpoints.GetClubActivity, cancellationToken);
        adminResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var adminResult = await adminResponse.Content.ReadFromJsonAsync<ClubActivityResult>(WebJsonOptions, cancellationToken);
        adminResult.ShouldNotBeNull();
        var adminContext = adminResult.Events.Single().Context.ShouldBeOfType<MembershipContext>();
        adminContext.MemberDisplayName.ShouldBe("Jordan Lee");
        adminContext.ApprovedByActorName.ShouldBe("Club Admin");
    }

    /// <summary>
    /// Verifies admin-only event kinds are hidden from non-admin members but visible to an
    /// administrator over HTTP.
    /// </summary>
    [Fact]
    public async Task GetActivity_HidesAdminOnlyKindsFromMembers_OverHttp()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var (club, adminUserId) = await CreateClubWithAdminAsync(adminClient, cancellationToken);
        using var memberClient = await CreateMemberClientAsync(club.ClubId, cancellationToken);

        var joinRequestPayload = JsonSerializer.Serialize(
            new JoinRequestContext { JoinRequestId = 1, RequesterDisplayName = "New Member" },
            typeof(ClubActivityContext));
        await SeedActivityEventsAsync(club.ClubId, adminUserId, count: 1, cancellationToken, kind: ActivityEventKind.JoinRequestSubmitted, payloadJson: joinRequestPayload);
        await SeedActivityEventsAsync(club.ClubId, adminUserId, count: 1, cancellationToken, kind: ActivityEventKind.CampaignOpened);

        using var memberResponse = await memberClient.GetAsync(ActivityEndpoints.GetClubActivity, cancellationToken);
        memberResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var memberResult = await memberResponse.Content.ReadFromJsonAsync<ClubActivityResult>(WebJsonOptions, cancellationToken);
        memberResult.ShouldNotBeNull();
        memberResult.Events.Select(item => item.Kind).ShouldNotContain(ActivityEventKind.JoinRequestSubmitted);
        memberResult.Events.Select(item => item.Kind).ShouldBe([ActivityEventKind.CampaignOpened]);

        using var adminResponse = await adminClient.GetAsync(ActivityEndpoints.GetClubActivity, cancellationToken);
        adminResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var adminResult = await adminResponse.Content.ReadFromJsonAsync<ClubActivityResult>(WebJsonOptions, cancellationToken);
        adminResult.ShouldNotBeNull();
        adminResult.Events.Select(item => item.Kind).ShouldBe([ActivityEventKind.CampaignOpened, ActivityEventKind.JoinRequestSubmitted]);
    }

    /// <summary>The web-default JSON options used by the ASP.NET Core serializers for response reads.</summary>
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Registers a new administrator, creates a club, and refreshes the membership cookie.</summary>
    /// <param name="adminClient">The client authenticated as the future club administrator.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club and the administrator's user identifier.</returns>
    private async Task<(ClubDto Club, long AdminUserId)> CreateClubWithAdminAsync(HttpClient adminClient, CancellationToken cancellationToken)
    {
        var email = SeedingHelpers.UniqueEmail("activity-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, email, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, clubId: null, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        long adminUserId;
        await using (var context = fixture.CreateAdminContext())
        {
            adminUserId = await context.Users
                .Where(user => user.NormalizedEmail == email.ToUpperInvariant())
                .Select(user => user.Id)
                .SingleAsync(cancellationToken);
        }

        return (club, adminUserId);
    }

    /// <summary>Registers a non-admin club member and refreshes the membership cookie.</summary>
    /// <param name="clubId">The club the member joins.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A client authenticated as a club member.</returns>
    private async Task<HttpClient> CreateMemberClientAsync(long clubId, CancellationToken cancellationToken)
    {
        var client = fixture.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("activity-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, clubId, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
        return client;
    }

    /// <summary>Seeds activity events directly into the club.</summary>
    /// <param name="clubId">The club identifier.</param>
    /// <param name="createdById">The user identifier stamped as the creator.</param>
    /// <param name="count">The number of rows to create.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <param name="kind">The event kind, defaults to a member-visible campaign kind.</param>
    /// <param name="payloadJson">The payload JSON, defaults to the family-matching payload.</param>
    private async Task SeedActivityEventsAsync(
        long clubId,
        long createdById,
        int count,
        CancellationToken cancellationToken,
        ActivityEventKind kind = ActivityEventKind.CampaignOpened,
        string? payloadJson = null)
    {
        await using var context = fixture.CreateAdminContext();
        var payload = payloadJson ?? JsonSerializer.Serialize(
            new CampaignLifecycleContext { CampaignId = 1, CampaignName = "Campaign" },
            typeof(ClubActivityContext));
        for (var index = 0; index < count; index++)
        {
            context.ActivityEvents.Add(new ActivityEventEntity
            {
                ClubId = clubId,
                EventKind = kind,
                IsAdminOnly = kind is ActivityEventKind.JoinRequestSubmitted
                    or ActivityEventKind.JoinRequestCancelled
                    or ActivityEventKind.JoinRequestRejected
                    or ActivityEventKind.CampaignDraftCreated
                    or ActivityEventKind.CampaignDraftDeleted,
                CampaignId = kind == ActivityEventKind.CampaignOpened ? 1 : null,
                ActorUserId = createdById,
                ActorDisplayName = "Actor",
                PayloadJson = payload,
                CreatedById = createdById,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
