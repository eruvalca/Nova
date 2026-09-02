using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Account;
using Nova.Shared.Features.Activity;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Verifies the campaign-close concurrency contract at the HTTP boundary: two administrators closing
/// the same ready campaign simultaneously produce exactly one success and one actionable conflict,
/// with the winner's closure provenance and single lifecycle event durably persisted.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignLifecycleRaceHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies two administrators closing the same ready campaign yield one 204 and one 409, and the
    /// persisted closure provenance matches the winner.
    /// </summary>
    [Fact]
    public async Task ConcurrentAdminCloses_YieldOneSuccessOneConflict_WithWinnerPersisted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var firstClient = fixture.CreateNovaHttpClient();
        var firstEmail = SeedingHelpers.UniqueEmail("lifecycle-race-first");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            firstClient, firstEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, firstEmail, clubId: null, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(firstClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(firstClient, cancellationToken);

        using var secondClient = fixture.CreateNovaHttpClient();
        var secondEmail = SeedingHelpers.UniqueEmail("lifecycle-race-second");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            secondClient, secondEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, secondEmail, club.ClubId, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(secondClient, cancellationToken);

        long firstUserId;
        long secondUserId;
        await using (var context = fixture.CreateAdminContext())
        {
            firstUserId = (await context.Users.SingleAsync(
                user => user.NormalizedEmail == firstEmail.ToUpperInvariant(), cancellationToken)).Id;
            secondUserId = (await context.Users.SingleAsync(
                user => user.NormalizedEmail == secondEmail.ToUpperInvariant(), cancellationToken)).Id;
        }

        using (var promotion = await firstClient.PostAsJsonAsync(
                   ClubEndpoints.AssignAdmin,
                   new AssignAdminInput { TargetUserId = secondUserId },
                   cancellationToken))
        {
            promotion.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await SeedingHelpers.RefreshClubMembershipCookieAsync(secondClient, cancellationToken);

        var seeded = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture,
            club.ClubId,
            firstEmail,
            "Lifecycle Race",
            participantCount: 1,
            PlacementOutcome.NotSelected,
            cancellationToken);

        var firstRequest = firstClient.PostAsync(
            CampaignEndpoints.CloseUrl(seeded.CampaignId),
            content: null,
            cancellationToken);
        var secondRequest = secondClient.PostAsync(
            CampaignEndpoints.CloseUrl(seeded.CampaignId),
            content: null,
            cancellationToken);

        using var firstResponse = await firstRequest;
        using var secondResponse = await secondRequest;

        var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };
        statuses.Count(status => status == HttpStatusCode.NoContent).ShouldBe(1);
        statuses.Count(status => status == HttpStatusCode.Conflict).ShouldBe(1);

        var winnerUserId = firstResponse.StatusCode == HttpStatusCode.NoContent
            ? firstUserId
            : secondUserId;

        var conflictResponse = firstResponse.StatusCode == HttpStatusCode.Conflict
            ? firstResponse
            : secondResponse;
        var conflictBody = await conflictResponse.Content.ReadAsStringAsync(cancellationToken);
        conflictBody.ShouldContain("The campaign is already closed.");

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seeded.CampaignId, cancellationToken);
        persisted.Status.ShouldBe(CampaignStatus.Closed);
        persisted.ClosedAt.ShouldNotBeNull();
        persisted.ClosedById.ShouldBe(winnerUserId);

        var closedEvents = await verify.ActivityEvents
            .Where(candidate => candidate.CampaignId == seeded.CampaignId
                && candidate.EventKind == ActivityEventKind.CampaignClosed)
            .ToListAsync(cancellationToken);
        closedEvents.Count.ShouldBe(1);
    }
}
