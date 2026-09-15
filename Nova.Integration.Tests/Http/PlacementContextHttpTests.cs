using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Http;

public sealed partial class CampaignPlacementHttpTests
{
    [Fact]
    public async Task PlacementContextRouteRequiresAuthenticationAsync()
    {
        using var client = fixture.CreateNovaHttpClient();
        using var response = await client.GetAsync(PlacementContextEndpoints.Url(new GetPlacementContextInput
        {
            CampaignId = 1,
            PlayerCampaignAssignmentId = 1
        }), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PlacementContextRouteReturnsForbiddenForAuthenticatedUserWithoutClubAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-context-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(PlacementContextEndpoints.Url(new GetPlacementContextInput
        {
            CampaignId = 1,
            PlayerCampaignAssignmentId = 1
        }), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MemberReadsPlacementHistoryWithoutCursorAndInvalidCursorIsRejectedAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("placement-context-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(admin, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, null, cancellationToken);
        var club = await CreateClubAsync(admin, cancellationToken);
        await RefreshClubMembershipCookieAsync(admin, cancellationToken);
        var seed = await SeedPlacementDataAsync(club.ClubId, adminEmail, cancellationToken);
        using var member = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("placement-context-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(member, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(member, cancellationToken);
        var input = new UpdateCampaignPlacementInput(seed.AssignmentId, PlacementOutcome.Assigned, seed.TeamId,
            seed.ConcurrencyToken, Guid.CreateVersion7());
        using var save = await member.PutAsJsonAsync(CampaignEndpoints.UpdateCampaignPlacementUrl(seed.AssignmentId), input, cancellationToken);
        save.StatusCode.ShouldBe(HttpStatusCode.OK);
        var committed = await save.Content.ReadFromJsonAsync<PlacementMutationSuccess>(cancellationToken);
        using var replay = await member.PutAsJsonAsync(CampaignEndpoints.UpdateCampaignPlacementUrl(seed.AssignmentId), input, cancellationToken);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await replay.Content.ReadFromJsonAsync<PlacementMutationSuccess>(cancellationToken)).ShouldBe(committed);
        await using var db = fixture.CreateAdminContext();
        var campaignId = await db.PlayerCampaignAssignments.Where(row => row.PlayerCampaignAssignmentId == seed.AssignmentId)
            .Select(row => row.CampaignId).SingleAsync(cancellationToken);
        var contextInput = new GetPlacementContextInput { CampaignId = campaignId, PlayerCampaignAssignmentId = seed.AssignmentId };

        using var response = await member.GetAsync(PlacementContextEndpoints.Url(contextInput), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var context = await response.Content.ReadFromJsonAsync<PlacementContextResult>(cancellationToken);
        context.ShouldNotBeNull();
        context.History.Count.ShouldBe(1);
        context.History[0].Outcome.ShouldBe(PlacementOutcome.Assigned);
        context.History[0].CampaignId.ShouldBe(campaignId);
        context.NextEventId.ShouldBeNull();
        context.PreviousPlacement.ShouldBeNull();
        // Bypass canonical URL normalization to exercise rejection of malformed wire input.
        var invalidUrl = new Uri(PlacementContextEndpoints.Url(contextInput).OriginalString + "?beforeEventId=0", UriKind.Relative);
        using var invalid = await member.GetAsync(invalidUrl, cancellationToken);
        ((int)invalid.StatusCode).ShouldBeOneOf(400, 422);
        using var missing = await member.GetAsync(PlacementContextEndpoints.Url(contextInput with { PlayerCampaignAssignmentId = long.MaxValue }), cancellationToken);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await db.PlacementMutationReceipts.CountAsync(row => row.ClubId == club.ClubId, cancellationToken)).ShouldBe(1);
    }
}
