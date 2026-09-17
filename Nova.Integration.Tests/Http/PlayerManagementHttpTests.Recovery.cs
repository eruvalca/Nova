using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Features.Players;
using Shouldly;

namespace Nova.Integration.Tests.Http;

public sealed partial class PlayerManagementHttpTests
{
    /// <summary>Malformed/future identities are field errors; only a valid elapsed window is expiry.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("01994916-0000-4000-8000-000000000000")]
    [InlineData("ffffffff-ffff-7000-8000-000000000000")]
    [InlineData("01994916-0000-7000-0000-000000000000")]
    [InlineData("future")]
    [InlineData("expired")]
    public async Task CreationDistinguishesInvalidOperationsFromExpiryAsync(string identity)
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var club = await CreateAuthenticatedClubAsync(client, "operation-validation", ct);
        var operationId = identity switch
        {
            "future" => Guid.CreateVersion7(DateTimeOffset.UtcNow.AddHours(1)),
            "expired" => Guid.CreateVersion7(DateTimeOffset.UtcNow.AddHours(-25)),
            _ => Guid.Parse(identity)
        };
        using var response = await client.PostAsJsonAsync(PlayerEndpoints.Create, ValidCreateInput(club.ClubId) with { OperationId = operationId }, ct);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        if (string.Equals(identity, "expired", StringComparison.Ordinal))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            problem.GetProperty(PlayerCreationProblems.ReasonExtension).GetString().ShouldBe("expired");
            problem.TryGetProperty("errors", out _).ShouldBeFalse();
        }
        else
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            problem.GetProperty("errors").GetProperty(nameof(CreatePlayerInput.OperationId)).GetArrayLength().ShouldBeGreaterThan(0);
            problem.TryGetProperty(PlayerCreationProblems.ReasonExtension, out _).ShouldBeFalse();
        }
        problem.TryGetProperty(PlayerCreationProblems.NotCommittedExtension, out _).ShouldBeFalse();
        problem.TryGetProperty(PlayerCreationProblems.DuplicateExtension, out _).ShouldBeFalse();
        await using var db = fixture.CreateAdminContext();
        (await db.Players.CountAsync(x => x.ClubId == club.ClubId, ct)).ShouldBe(0);
        (await db.PlayerCreationReceipts.CountAsync(x => x.ClubId == club.ClubId, ct)).ShouldBe(0);
    }

    /// <summary>A live member cannot select another tenant for execution or receipt recovery.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateRejectsAnotherClubsApprovedMemberWithoutDisclosingOrWritingAsync(bool receiptExists)
    {
        var ct = TestContext.Current.CancellationToken;
        using var targetAdminClient = fixture.CreateNovaHttpClient();
        using var ownAdminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var targetClub = await CreateAuthenticatedClubAsync(targetAdminClient, "target", ct);
        var ownClub = await CreateAuthenticatedClubAsync(ownAdminClient, "own", ct);
        var memberEmail = UniqueEmail("cross-club-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, ct);
        await UpdateUserAsync(memberEmail, "Live", "Member", ownClub.ClubId, ct);
        await RefreshClubMembershipCookieAsync(memberClient, ct);
        var input = ValidCreateInput(targetClub.ClubId);
        if (receiptExists)
        {
            using var original = await targetAdminClient.PostAsJsonAsync(PlayerEndpoints.Create, input, ct);
            original.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var denied = await memberClient.PostAsJsonAsync(PlayerEndpoints.Create, input, ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var problem = await denied.Content.ReadFromJsonAsync<JsonElement>(ct);
        problem.GetProperty("status").GetInt32().ShouldBe(403);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.TryGetProperty("player", out _).ShouldBeFalse();
        problem.TryGetProperty("enrollment", out _).ShouldBeFalse();
        problem.TryGetProperty(PlayerCreationProblems.ReasonExtension, out _).ShouldBeFalse();
        problem.TryGetProperty(PlayerCreationProblems.DuplicateExtension, out _).ShouldBeFalse();
        problem.TryGetProperty(PlayerCreationProblems.NotCommittedExtension, out _).ShouldBeFalse();
        await using var db = fixture.CreateAdminContext();
        var expectedClubs = receiptExists ? new[] { targetClub.ClubId } : Array.Empty<long>();
        (await db.Players.Where(x => x.CreationOperationId == input.OperationId)
            .Select(x => x.ClubId).ToArrayAsync(ct)).ShouldBe(expectedClubs);
        (await db.PlayerCreationReceipts.Where(x => x.OperationId == input.OperationId)
            .Select(x => x.ClubId).ToArrayAsync(ct)).ShouldBe(expectedClubs);

        // The same live member and operation can execute in the authorized club; the denial
        // neither reflects a missing membership nor reserves the operation in that tenant.
        using var allowed = await memberClient.PostAsJsonAsync(PlayerEndpoints.Create, input with { ClubId = ownClub.ClubId }, ct);
        allowed.StatusCode.ShouldBe(HttpStatusCode.Created);
        var completion = (await allowed.Content.ReadFromJsonAsync<PlayerCreationCompletion>(ct)).ShouldNotBeNull();
        completion.OperationId.ShouldBe(input.OperationId);
        completion.Player.ClubId.ShouldBe(ownClub.ClubId);
        (await db.Players.CountAsync(x => x.ClubId == ownClub.ClubId && x.CreationOperationId == input.OperationId, ct)).ShouldBe(1);
        (await db.PlayerCreationReceipts.CountAsync(x => x.ClubId == ownClub.ClubId && x.OperationId == input.OperationId, ct)).ShouldBe(1);
    }

    private static async Task<ClubDto> CreateAuthenticatedClubAsync(HttpClient client, string prefix, CancellationToken ct)
    {
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, UniqueEmail(prefix), Password, ct);
        var club = await CreateClubAsync(client, $"Cross-club {prefix}", "Austin", "TX", ct);
        await RefreshClubMembershipCookieAsync(client, ct);
        return club;
    }

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
