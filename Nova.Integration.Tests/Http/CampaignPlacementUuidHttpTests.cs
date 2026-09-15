using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

public sealed partial class CampaignPlacementHttpTests
{
    [Fact]
    public async Task FuturePlacementOperationReturnsClockGuidanceWithoutSettlementProofAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-future-uuid");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);
        var id = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddDays(1));
        var input = new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token, id);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PutAsJsonAsync(CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId), input, cancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await response.ToServiceProblemAsync(cancellationToken);
            problem.Kind.ShouldBe(ServiceProblemKind.Validation);
            PlacementMutationRejection.IsFutureDated(problem, id).ShouldBeTrue();
            PlacementMutationRejection.IsNotCommitted(problem, id).ShouldBeFalse();
            PlacementMutationRejection.IsExpired(problem, id).ShouldBeFalse();
        }
        await using var db = fixture.CreateAdminContext();
        var saved = await db.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        saved.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        saved.ConcurrencyToken.ShouldBe(token);
        (await db.PlacementMutationReceipts.CountAsync(row => row.ClubId == club.ClubId, cancellationToken)).ShouldBe(0);
        (await db.ActivityEvents.CountAsync(row => row.ClubId == club.ClubId && row.PlayerId == saved.PlayerId, cancellationToken)).ShouldBe(0);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData('0')]
    [InlineData('7')]
    [InlineData('c')]
    [InlineData('f')]
    public async Task PlacementHttpRejectsInvalidUuidVariantWithoutEffectsAsync(char variant)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-uuid");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);
        var chars = Guid.CreateVersion7().ToString("N").ToCharArray();
        chars[16] = variant;
        var id = Guid.ParseExact(new string(chars), "N");
        using var response = await client.PutAsJsonAsync(CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token, id), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(cancellationToken)).ShouldContain("OperationId");
        await using var db = fixture.CreateAdminContext();
        var saved = await db.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        saved.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        saved.ConcurrencyToken.ShouldBe(token);
        (await db.PlacementMutationReceipts.CountAsync(row => row.ClubId == club.ClubId, cancellationToken)).ShouldBe(0);
    }
}
