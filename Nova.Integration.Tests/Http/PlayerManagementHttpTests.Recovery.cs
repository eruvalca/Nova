using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Features.Players;
using Shouldly;

namespace Nova.Integration.Tests.Http;

public sealed partial class PlayerManagementHttpTests
{
    /// <summary>Durable evidence survives a fresh HTTP client, mutable profile changes, and exact-request rejection.</summary>
    [Fact]
    public async Task CreationReplaysOriginalEvidenceThroughFreshClientAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("receipt");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, ct);
        var club = await CreateClubAsync(client, "Receipt club", "Austin", "TX", ct);
        using var refresh = await client.GetAsync(new Uri($"{ClubEndpoints.Complete}?returnUrl=/dashboard", UriKind.Relative), ct);
        refresh.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
        using var recoveredClient = fixture.CreateNovaHttpClient();
        recoveredClient.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(x => x.Split(';')[0])));
        await SeedingHelpers.SeedCampaignWithParticipantsAsync(fixture, club.ClubId, email, "Receipt campaign", 1, Nova.SharedKernel.Enums.PlacementOutcome.Undecided, ct);
        var input = ValidCreateInput(club.ClubId);
        using var response = await client.PostAsJsonAsync(PlayerEndpoints.Create, input, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var original = (await response.Content.ReadFromJsonAsync<PlayerCreationCompletion>(ct)).ShouldNotBeNull();
        original.Enrollment.ShouldNotBeNull();
        await using (var db = fixture.CreateAdminContext())
        {
            original.Enrollment.CampaignName.ShouldBe(await db.Campaigns.Where(x => x.CampaignId == original.Enrollment.CampaignId)
                .Select(x => x.Name).SingleAsync(ct));
            (await db.Players.SingleAsync(x => x.PlayerId == original.Player.PlayerId, ct)).FirstName = "Later";
            await db.SaveChangesAsync(ct);
        }
        using var replay = await recoveredClient.PostAsJsonAsync(PlayerEndpoints.Create, input, ct);
        replay.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await replay.Content.ReadFromJsonAsync<PlayerCreationCompletion>(ct)).ShouldBe(original);
        using var mismatch = await recoveredClient.PostAsJsonAsync(PlayerEndpoints.Create, input with { FirstName = "Different" }, ct);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await mismatch.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty(PlayerCreationProblems.ReasonExtension).GetString().ShouldBe("operationMismatch");
        problem.TryGetProperty(PlayerCreationProblems.NotCommittedExtension, out _).ShouldBeFalse();
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        var duplicateInput = input with { OperationId = Guid.CreateVersion7(), FirstName = " later " };
        using var duplicate = await recoveredClient.PostAsJsonAsync(PlayerEndpoints.Create, duplicateInput, ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var rejection = await duplicate.Content.ReadFromJsonAsync<JsonElement>(ct);
        rejection.GetProperty(PlayerCreationProblems.ReasonExtension).GetString().ShouldBe("possibleDuplicate");
        rejection.GetProperty(PlayerCreationProblems.DuplicateExtension).GetProperty("playerId").GetInt64().ShouldBe(original.Player.PlayerId);
        rejection.GetProperty(PlayerCreationProblems.NotCommittedExtension).GetGuid().ShouldBe(duplicateInput.OperationId);

        await AssertRemovedMemberDeniedAsync(recoveredClient, email, input, original.Player.PlayerId, ct);
    }

    private async Task AssertRemovedMemberDeniedAsync(HttpClient recoveredClient, string email, CreatePlayerInput input, long playerId, CancellationToken ct)
    {
        await UpdateUserAsync(email, "Removed", "Member", clubId: null, ct);
        using var deniedReplay = await recoveredClient.PostAsJsonAsync(PlayerEndpoints.Create, input, ct);
        deniedReplay.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var deniedEdit = await recoveredClient.PutAsJsonAsync(PlayerEndpoints.UpdateUrl(playerId),
            new UpdatePlayerInput
            {
                PlayerId = playerId,
                FirstName = "Denied",
                LastName = input.LastName,
                DateOfBirth = input.DateOfBirth,
                GraduationYear = input.GraduationYear
            }, ct);
        deniedEdit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var deniedArchive = await recoveredClient.PostAsync(new Uri(PlayerEndpoints.ArchiveUrl(playerId), UriKind.Relative), null, ct);
        deniedArchive.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var deniedRestore = await recoveredClient.PostAsync(new Uri(PlayerEndpoints.RestoreUrl(playerId), UriKind.Relative), null, ct);
        deniedRestore.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
