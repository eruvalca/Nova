using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Features.Players;
using Shouldly;

namespace Nova.Integration.Tests.Http;

public sealed partial class PlayerManagementHttpTests
{
    /// <summary>Duplicate recovery preserves the original camelCase wire evidence after the matching record changes.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateReplayPreservesOriginalHttpShapeAsync(bool archived)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var club = await CreateAuthenticatedClubAsync(client, "duplicate-replay", ct);
        var input = ValidCreateInput(club.ClubId);
        using var created = await client.PostAsJsonAsync(PlayerEndpoints.Create, input, ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var completion = (await created.Content.ReadFromJsonAsync<PlayerCreationCompletion>(ct)).ShouldNotBeNull();
        await ChangeDuplicatePlayerAsync(completion.Player.PlayerId, input.FirstName, archived, ct);
        var duplicateInput = input with { OperationId = Guid.CreateVersion7() };
        using var rejected = await client.PostAsJsonAsync(PlayerEndpoints.Create, duplicateInput, ct);
        var original = await ReadDuplicateEvidenceAsync(rejected, duplicateInput.OperationId, completion.Player.PlayerId, archived, ct);

        await ChangeDuplicatePlayerAsync(completion.Player.PlayerId, "Later identity", !archived, ct);
        using var refresh = await client.GetAsync(new Uri($"{ClubEndpoints.Complete}?returnUrl=/dashboard", UriKind.Relative), ct);
        refresh.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
        using var freshClient = fixture.CreateNovaHttpClient();
        freshClient.DefaultRequestHeaders.Add("Cookie", string.Join("; ", cookies.Select(value => value.Split(';')[0])));
        using var replay = await freshClient.PostAsJsonAsync(PlayerEndpoints.Create, duplicateInput, ct);
        var recovered = await ReadDuplicateEvidenceAsync(replay, duplicateInput.OperationId, completion.Player.PlayerId, archived, ct);
        JsonNode.DeepEquals(recovered, original).ShouldBeTrue();
        await using var db = fixture.CreateAdminContext();
        (await db.Players.CountAsync(player => player.ClubId == club.ClubId, ct)).ShouldBe(1);
        (await db.PlayerCreationReceipts.CountAsync(receipt => receipt.ClubId == club.ClubId, ct)).ShouldBe(2);
        (await db.PlayerCampaignAssignments.CountAsync(assignment => assignment.ClubId == club.ClubId, ct)).ShouldBe(0);
    }

    private async Task ChangeDuplicatePlayerAsync(long playerId, string firstName, bool archived, CancellationToken ct)
    {
        await using var db = fixture.CreateAdminContext();
        var player = await db.Players.SingleAsync(candidate => candidate.PlayerId == playerId, ct);
        player.FirstName = firstName;
        player.LifecycleStatus = archived ? LifecycleStatus.Archived : LifecycleStatus.Active;
        player.ArchivedAt = archived ? DateTimeOffset.UtcNow : null;
        player.ArchivedById = archived ? player.CreatedById : null;
        await db.SaveChangesAsync(ct);
    }

    private static async Task<JsonObject> ReadDuplicateEvidenceAsync(HttpResponseMessage response, Guid operationId,
        long playerId, bool archived, CancellationToken ct)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)).ShouldNotBeNull().AsObject();
        problem["traceId"].ShouldNotBeNull().GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        problem[PlayerCreationProblems.ReasonExtension].ShouldNotBeNull().GetValue<string>().ShouldBe("possibleDuplicate");
        problem[PlayerCreationProblems.NotCommittedExtension].ShouldNotBeNull().GetValue<Guid>().ShouldBe(operationId);
        var duplicate = problem[PlayerCreationProblems.DuplicateExtension].ShouldNotBeNull().AsObject();
        duplicate.Select(property => property.Key).Order(StringComparer.Ordinal).ShouldBe(["lifecycleStatus", "playerId"]);
        duplicate["playerId"].ShouldNotBeNull().GetValue<long>().ShouldBe(playerId);
        duplicate["lifecycleStatus"].ShouldNotBeNull().GetValue<int>().ShouldBe((int)(archived ? LifecycleStatus.Archived : LifecycleStatus.Active));
        problem.Remove("traceId").ShouldBeTrue();
        return problem;
    }
}
