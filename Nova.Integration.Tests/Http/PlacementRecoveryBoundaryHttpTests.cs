using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

public sealed partial class CampaignPlacementHttpTests
{
    [Fact]
    public async Task PlacementContextHidesForeignCampaignsAndParticipantsOverHttpAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = fixture.CreateNovaHttpClient();
        var ownerEmail = UniqueEmail("context-isolation-owner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(owner, ownerEmail, Password, cancellationToken);
        await UpdateUserAsync(ownerEmail, null, cancellationToken);
        var ownerClub = await CreateClubAsync(owner, cancellationToken);
        await RefreshClubMembershipCookieAsync(owner, cancellationToken);
        var owned = await SeedPlacementDataAsync(ownerClub.ClubId, ownerEmail, cancellationToken);
        using var save = await owner.PutAsJsonAsync(CampaignEndpoints.UpdateCampaignPlacementUrl(owned.AssignmentId),
            new UpdateCampaignPlacementInput(owned.AssignmentId, PlacementOutcome.Assigned, owned.TeamId, owned.ConcurrencyToken, Guid.CreateVersion7()), cancellationToken);
        save.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var outsider = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("context-isolation-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(outsider, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(otherEmail, null, cancellationToken);
        var otherClub = await CreateClubAsync(outsider, cancellationToken);
        await RefreshClubMembershipCookieAsync(outsider, cancellationToken);
        otherClub.ClubId.ShouldNotBe(ownerClub.ClubId);
        var other = await SeedPlacementDataAsync(otherClub.ClubId, otherEmail, cancellationToken);
        await using var db = fixture.CreateAdminContext();
        var ownedCampaign = await db.PlayerCampaignAssignments.Where(row => row.PlayerCampaignAssignmentId == owned.AssignmentId)
            .Select(row => row.CampaignId).SingleAsync(cancellationToken);
        var otherCampaign = await db.PlayerCampaignAssignments.Where(row => row.PlayerCampaignAssignmentId == other.AssignmentId)
            .Select(row => row.CampaignId).SingleAsync(cancellationToken);
        var ownedInput = new GetPlacementContextInput { CampaignId = ownedCampaign, PlayerCampaignAssignmentId = owned.AssignmentId };
        using var readable = await owner.GetAsync(PlacementContextEndpoints.Url(ownedInput), cancellationToken);
        readable.StatusCode.ShouldBe(HttpStatusCode.OK);
        var evidence = (await readable.Content.ReadFromJsonAsync<PlacementContextResult>(cancellationToken)).ShouldNotBeNull();
        var change = evidence.History.ShouldHaveSingleItem();
        change.Outcome.ShouldBe(PlacementOutcome.Assigned);
        change.TeamName.ShouldNotBeNullOrWhiteSpace();
        using var ownRead = await outsider.GetAsync(PlacementContextEndpoints.Url(new GetPlacementContextInput
        { CampaignId = otherCampaign, PlayerCampaignAssignmentId = other.AssignmentId }), cancellationToken);
        ownRead.StatusCode.ShouldBe(HttpStatusCode.OK);

        GetPlacementContextInput[] deniedInputs =
        [
            ownedInput,
            ownedInput with { CampaignId = otherCampaign },
            ownedInput with { PlayerCampaignAssignmentId = other.AssignmentId },
            new() { CampaignId = otherCampaign, PlayerCampaignAssignmentId = long.MaxValue }
        ];
        foreach (var input in deniedInputs)
        {
            await AssertPlacementContextHiddenAsync(outsider, input, change, cancellationToken);
        }
        (await db.ActivityEvents.CountAsync(row => row.ClubId == ownerClub.ClubId && row.CampaignId == ownedCampaign, cancellationToken)).ShouldBe(1);
    }

    private static async Task AssertPlacementContextHiddenAsync(HttpClient client, GetPlacementContextInput input,
        PlacementHistoryItem change, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(PlacementContextEndpoints.Url(input), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        problem.Detail.ShouldBe(ServiceProblem.NotFound().Detail);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("history", out _).ShouldBeFalse();
        document.RootElement.TryGetProperty("previousPlacement", out _).ShouldBeFalse();
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        body.ShouldNotContain(change.CampaignName);
        body.ShouldNotContain(change.TeamName.ShouldNotBeNull());
    }

    [Fact]
    public async Task DurablePlacementRejectionPreservesItsOperationMarkerOnHttpReplayAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-rejection-replay");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seed = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken, archivedTeam: true);
        var input = new UpdateCampaignPlacementInput(seed.AssignmentId, PlacementOutcome.Assigned, seed.TeamId,
            seed.ConcurrencyToken, Guid.CreateVersion7());
        await using var db = fixture.CreateAdminContext();
        string? storedResult = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await client.PutAsJsonAsync(CampaignEndpoints.UpdateCampaignPlacementUrl(seed.AssignmentId), input, cancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var problem = await response.ToServiceProblemAsync(cancellationToken);
            problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
            problem.Detail.ShouldBe("Archived teams cannot receive new placements.");
            PlacementMutationRejection.IsNotCommitted(problem, input.OperationId).ShouldBeTrue();
            PlacementMutationRejection.IsNotCommitted(problem, Guid.CreateVersion7()).ShouldBeFalse();
            problem.Extensions.ShouldNotBeNull()[PlacementMutationRejection.OperationIdExtension]!.ToString().ShouldBe(input.OperationId.ToString("D"));
            var receipt = await db.PlacementMutationReceipts.AsNoTracking().SingleAsync(row => row.ClubId == club.ClubId, cancellationToken);
            receipt.OperationId.ShouldBe(input.OperationId);
            if (storedResult is null) { storedResult = receipt.ResultJson; }
            else { receipt.ResultJson.ShouldBe(storedResult); }
            var team = await db.Teams.SingleAsync(row => row.TeamId == seed.TeamId, cancellationToken);
            team.LifecycleStatus = LifecycleStatus.Active;
            team.ArchivedAt = null;
            team.ArchivedById = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        var unchanged = await db.PlayerCampaignAssignments.AsNoTracking().SingleAsync(row => row.PlayerCampaignAssignmentId == seed.AssignmentId, cancellationToken);
        unchanged.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        unchanged.ConcurrencyToken.ShouldBe(seed.ConcurrencyToken);
        unchanged.TeamId.ShouldBeNull();
        (await db.ActivityEvents.CountAsync(row => row.ClubId == club.ClubId && row.PlayerId == unchanged.PlayerId, cancellationToken)).ShouldBe(0);
        using var deliberate = await client.PutAsJsonAsync(CampaignEndpoints.UpdateCampaignPlacementUrl(seed.AssignmentId),
            input with { OperationId = Guid.CreateVersion7() }, cancellationToken);
        deliberate.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await db.PlacementMutationReceipts.CountAsync(row => row.ClubId == club.ClubId, cancellationToken)).ShouldBe(2);
        (await db.ActivityEvents.CountAsync(row => row.ClubId == club.ClubId && row.PlayerId == unchanged.PlayerId, cancellationToken)).ShouldBe(1);
    }
}
