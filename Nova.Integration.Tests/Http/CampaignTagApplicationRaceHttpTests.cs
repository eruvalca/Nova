using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Cross-slice HTTP coverage for the duplicate tag-application race: when two approved club
/// members apply the same tag to the same assignment concurrently, exactly one request
/// succeeds, the other receives a clear conflict, and exactly one durable row exists.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignTagApplicationRaceHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies that two concurrent tag applications for the same (assignment, tag) pair yield
    /// one Created response, one Conflict response, and a single durable database row.
    /// </summary>
    [Fact]
    public async Task ParallelTagApplication_ForSameAssignmentAndTag_YieldsOneCreatedOneConflict_WithSingleDurableRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (firstClient, secondClient, adminEmail, assignmentId) = await SeedTwoMemberClubWithTagAsync(
            "tag-race", cancellationToken);
        var tagId = await SeedingHelpers.InsertTagDefinitionAsync(fixture, assignmentId, adminEmail, "Winger", "#00CC00", cancellationToken);

        var applyInput = () => new ApplyCampaignTagApplicationInput
        {
            PlayerCampaignAssignmentId = assignmentId,
            PlayerTagId = tagId
        };

        // Start both requests before awaiting either so they race through the server
        // simultaneously; either request may win, so both orderings are tolerated.
        var applyA = firstClient.PostAsJsonAsync(CampaignEndpoints.ApplyCampaignTagApplication, applyInput(), cancellationToken);
        var applyB = secondClient.PostAsJsonAsync(CampaignEndpoints.ApplyCampaignTagApplication, applyInput(), cancellationToken);
        using var responseA = await applyA;
        using var responseB = await applyB;

        var statuses = new[] { responseA.StatusCode, responseB.StatusCode };
        statuses.Count(status => status == HttpStatusCode.Created).ShouldBe(1);
        statuses.Count(status => status == HttpStatusCode.Conflict).ShouldBe(1);

        await using var context = fixture.CreateAdminContext();
        var durableRows = await context.CampaignTagApplications
            .Where(candidate => candidate.PlayerCampaignAssignmentId == assignmentId
                && candidate.PlayerTagId == tagId)
            .ToListAsync(cancellationToken);
        durableRows.Count.ShouldBe(1);
    }

    /// <summary>
    /// Seeds two approved members in one club and an active campaign with one participant.
    /// </summary>
    /// <param name="prefix">A stable e-mail prefix.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The two member clients, the admin e-mail, and the assignment identifier.</returns>
    private async Task<(HttpClient FirstClient, HttpClient SecondClient, string AdminEmail, long AssignmentId)>
        SeedTwoMemberClubWithTagAsync(string prefix, CancellationToken cancellationToken)
    {
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail($"{prefix}-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var firstClient = fixture.CreateNovaHttpClient();
        var firstEmail = SeedingHelpers.UniqueEmail($"{prefix}-first");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(firstClient, firstEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, firstEmail, club.ClubId, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(firstClient, cancellationToken);

        var secondClient = fixture.CreateNovaHttpClient();
        var secondEmail = SeedingHelpers.UniqueEmail($"{prefix}-second");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(secondClient, secondEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, secondEmail, club.ClubId, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(secondClient, cancellationToken);

        var seeded = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture, club.ClubId, adminEmail, prefix, participantCount: 1, placementOutcome: PlacementOutcome.Undecided, cancellationToken);
        return (firstClient, secondClient, adminEmail, seeded.AssignmentIds[0]);
    }
}
