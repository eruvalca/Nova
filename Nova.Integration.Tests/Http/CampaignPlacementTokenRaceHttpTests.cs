using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Account;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Clubs;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Verifies optimistic concurrency for simultaneous administrator placement updates at the HTTP
/// boundary.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignPlacementTokenRaceHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies two administrators using the same expected token yield one winner and one conflict.
    /// </summary>
    [Fact]
#pragma warning disable MA0051 // Keep this complete setup, operation, and assertion sequence together as one regression scenario.
    public async Task ConcurrentAdminUpdatesSameExpectedTokenYieldOneSuccessOneConflictWithWinnerPersistedAsync()
#pragma warning restore MA0051
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var firstClient = fixture.CreateNovaHttpClient();
        var firstEmail = SeedingHelpers.UniqueEmail("placement-token-race-first");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            firstClient,
            firstEmail,
            Password,
            cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, firstEmail, clubId: null, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(firstClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(firstClient, cancellationToken);

        using var secondClient = fixture.CreateNovaHttpClient();
        var secondEmail = SeedingHelpers.UniqueEmail("placement-token-race-second");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            secondClient,
            secondEmail,
            Password,
            cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, secondEmail, club.ClubId, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(secondClient, cancellationToken);

        long secondUserId;
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using (var context = fixture.CreateAdminContext())
#pragma warning restore MA0004
        {
            secondUserId = await context.Users
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
                .Where(user => user.NormalizedEmail == secondEmail.ToUpperInvariant())
#pragma warning restore CA1862
                .Select(user => user.Id)
                .SingleAsync(cancellationToken);
        }

        using (var promotion = await firstClient.PostAsync(
new Uri(ClubEndpoints.PromoteMemberUrl(secondUserId), UriKind.RelativeOrAbsolute), null, cancellationToken))
        {
            promotion.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await SeedingHelpers.RefreshClubMembershipCookieAsync(secondClient, cancellationToken);
        var seeded = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture,
            club.ClubId,
            firstEmail,
            "Placement Token Race",
            participantCount: 1,
            PlacementOutcome.Undecided,
            cancellationToken);
        var teamId = await SeedingHelpers.InsertTeamAsync(
            fixture,
            club.ClubId,
            firstEmail,
            $"Race Team {Guid.NewGuid():N}",
            2028,
            cancellationToken);

        Guid expectedToken;
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using (var context = fixture.CreateAdminContext())
#pragma warning restore MA0004
        {
            expectedToken = await context.PlayerCampaignAssignments
                .Where(assignment => assignment.PlayerCampaignAssignmentId == seeded.AssignmentIds[0])
                .Select(assignment => assignment.ConcurrencyToken)
                .SingleAsync(cancellationToken);
        }

#pragma warning disable CA2025 // Both concurrent requests are awaited with Task.WhenAll before client disposal; successful responses are also disposed on failure.
        var firstRequest = firstClient.PutAsJsonAsync(
#pragma warning restore CA2025
            CampaignEndpoints.UpdateCampaignPlacementUrl(seeded.AssignmentIds[0]),
            new UpdateCampaignPlacementInput(
                seeded.AssignmentIds[0],
                PlacementOutcome.Assigned,
                teamId,
                expectedToken),
            cancellationToken);
#pragma warning disable CA2025 // Both concurrent requests are awaited with Task.WhenAll before client disposal; successful responses are also disposed on failure.
        var secondRequest = secondClient.PutAsJsonAsync(
#pragma warning restore CA2025
            CampaignEndpoints.UpdateCampaignPlacementUrl(seeded.AssignmentIds[0]),
            new UpdateCampaignPlacementInput(
                seeded.AssignmentIds[0],
                PlacementOutcome.Withdrawn,
                teamId: null,
                expectedToken),
            cancellationToken);

        try
        {
            await Task.WhenAll(firstRequest, secondRequest);
        }
        catch
        {
            // Both requests have finished; dispose any successful response before propagating the failure.
            if (firstRequest.IsCompletedSuccessfully)
            {
                (await firstRequest).Dispose();
            }
            if (secondRequest.IsCompletedSuccessfully)
            {
                (await secondRequest).Dispose();
            }
            throw;
        }

        using var firstResponse = await firstRequest;
        using var secondResponse = await secondRequest;
        var statuses = new[] { firstResponse.StatusCode, secondResponse.StatusCode };
        statuses.Count(status => status == HttpStatusCode.OK).ShouldBe(1);
        statuses.Count(status => status == HttpStatusCode.Conflict).ShouldBe(1);

        var winnerResponse = firstResponse.StatusCode == HttpStatusCode.OK ? firstResponse : secondResponse;
        var winnerOutcome = firstResponse.StatusCode == HttpStatusCode.OK
            ? PlacementOutcome.Assigned
            : PlacementOutcome.Withdrawn;
        var winner = await winnerResponse.Content.ReadFromJsonAsync<PlacementMutationSuccess>(cancellationToken);
        winner.ConcurrencyToken.ShouldNotBe(expectedToken);

        var conflictResponse = firstResponse.StatusCode == HttpStatusCode.Conflict ? firstResponse : secondResponse;
        var conflictBody = await conflictResponse.Content.ReadAsStringAsync(cancellationToken);
        conflictBody.ShouldContain("The placement was changed by another user. Reload it and try again.");

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.PlayerCampaignAssignments
            .SingleAsync(
                assignment => assignment.PlayerCampaignAssignmentId == seeded.AssignmentIds[0],
                cancellationToken);
        persisted.PlacementOutcome.ShouldBe(winnerOutcome);
        persisted.TeamId.ShouldBe(winnerOutcome == PlacementOutcome.Assigned ? teamId : null);
        persisted.ConcurrencyToken.ShouldBe(winner.ConcurrencyToken);
    }
}
