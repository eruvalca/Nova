using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Players;
using Nova.Shared.Security;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>Exercises the registered commit route through real multipart transport, Identity, and persistence.</summary>
/// <param name="fixture">The shared Aspire application.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class PlayerImportCommitHttpTests(NovaAppHostFixture fixture)
{
    /// <summary>Anonymous callers cannot execute the registered route.</summary>
    [Fact]
    public async Task Commit_ReturnsUnauthorized_ForAnonymousCaller()
    {
        using var client = fixture.CreateNovaHttpClient();
        using var form = Form(Csv());
        using var response = await client.PostAsync(PlayerEndpoints.ImportCommit, form, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Club membership alone does not permit committing an import.</summary>
    [Fact]
    public async Task Commit_ReturnsForbidden_ForOrdinaryMember()
    {
        using var client = fixture.CreateNovaHttpClient();
        _ = await AuthenticateAsync(client, administrator: false);
        using var form = Form(Csv());
        using var response = await client.PostAsync(PlayerEndpoints.ImportCommit, form, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>A mixed import succeeds and identical multipart replay returns the immutable result once.</summary>
    [Fact]
    public async Task Commit_PersistsMixedResults_AndReplaysOriginalCompletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var clubId = await AuthenticateAsync(client);
        var bytes = Csv("Taylor,Stone,2013-02-03,,,2031\r\nTaylor,Stone,2013-02-03,,,2031\r\n,Invalid,no-date,,,1999\r\n");
        var preview = await PreviewAsync(client, bytes);
        using var form = Form(bytes, preview);
        using var response = await client.PostAsync(PlayerEndpoints.ImportCommit, form, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var completion = await response.Content.ReadFromJsonAsync<PlayerImportCompletion>(cancellationToken);
        completion.ShouldNotBeNull();
        completion.OperationId.ShouldBe(preview.OperationId);
        completion.CreatedRows.ShouldBe(1);
        completion.SkippedDuplicateRows.ShouldBe(1);
        completion.SkippedInvalidRows.ShouldBe(1);
        completion.WaitingPlayers.ShouldBe(1);
        completion.Rows.Select(row => row.Status).ShouldBe([
            PlayerImportCommitRowStatus.Created, PlayerImportCommitRowStatus.SkippedDuplicateAtPreview,
            PlayerImportCommitRowStatus.SkippedInvalidAtPreview]);
        using var replayForm = Form(bytes, preview);
        using var replay = await client.PostAsync(PlayerEndpoints.ImportCommit, replayForm, cancellationToken);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonSerializer.Serialize(await replay.Content.ReadFromJsonAsync<PlayerImportCompletion>(cancellationToken))
            .ShouldBe(JsonSerializer.Serialize(completion));
        await using var db = fixture.CreateAdminContext();
        (await db.Players.CountAsync(player => player.ClubId == clubId, cancellationToken)).ShouldBe(1);
        (await db.PlayerImportReceipts.CountAsync(receipt => receipt.ClubId == clubId, cancellationToken)).ShouldBe(1);
        (await db.PlayerCampaignAssignments.CountAsync(assignment => assignment.ClubId == clubId, cancellationToken)).ShouldBe(0);
    }

    /// <summary>Missing or duplicated confirmation fields produce bounded trace-correlated validation errors.</summary>
    /// <param name="malformation">The malformed multipart shape.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("missing file")]
    [InlineData("missing identity")]
    [InlineData("bad operation")]
    [InlineData("bad token")]
    [InlineData("duplicate token")]
    [InlineData("oversized file")]
    public async Task Commit_RejectsMalformedMultipart_WithTraceId(string malformation)
    {
        using var client = fixture.CreateNovaHttpClient();
        _ = await AuthenticateAsync(client);
        var bytes = Csv();
        var preview = await PreviewAsync(client, bytes);
        using var form = malformation == "missing file" ? new MultipartFormDataContent()
            : Form(malformation == "oversized file" ? new byte[PlayerImportConstraints.MaxFileBytes + 1] : bytes);
        if (malformation != "missing identity")
        {
            form.Add(new StringContent(malformation == "bad operation" ? "invalid" : preview.OperationId.ToString()), PlayerImportConstraints.OperationIdFormFieldName);
            form.Add(new StringContent(malformation == "bad token" ? "tampered" : preview.ConfirmationToken), PlayerImportConstraints.ConfirmationTokenFormFieldName);
        }
        if (malformation == "duplicate token")
        {
            form.Add(new StringContent(preview.ConfirmationToken), PlayerImportConstraints.ConfirmationTokenFormFieldName);
        }

        using var response = await client.PostAsync(PlayerEndpoints.ImportCommit, form, TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, malformation == "bad token" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest);
    }

    /// <summary>The commit route independently enforces transport limits and supported media type.</summary>
    /// <param name="oversized">Whether the request exceeds the transport bound.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_EnforcesTransportBoundary_WithTraceId(bool oversized)
    {
        using var client = fixture.CreateNovaHttpClient();
        _ = await AuthenticateAsync(client);
        using HttpContent content = oversized ? Form(new byte[PlayerImportConstraints.MaxRequestBytes + 1]) : JsonContent.Create(new { file = "not multipart" });
        using var request = new HttpRequestMessage(HttpMethod.Post, PlayerEndpoints.ImportCommit) { Content = content };
        request.Headers.ExpectContinue = true;
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, oversized ? HttpStatusCode.RequestEntityTooLarge : HttpStatusCode.UnsupportedMediaType);
    }

    /// <summary>Changing bytes after review is rejected and creates no receipt.</summary>
    [Fact]
    public async Task Commit_RejectsChangedBytes_AfterPreview()
    {
        using var client = fixture.CreateNovaHttpClient();
        var clubId = await AuthenticateAsync(client);
        var bytes = Csv();
        var preview = await PreviewAsync(client, bytes);
        using var form = Form([.. bytes, (byte)'\n'], preview);
        using var response = await client.PostAsync(PlayerEndpoints.ImportCommit, form, TestContext.Current.CancellationToken);
        await AssertProblemAsync(response, HttpStatusCode.Conflict);
        await using var db = fixture.CreateAdminContext();
        (await db.PlayerImportReceipts.CountAsync(receipt => receipt.ClubId == clubId, TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    /// <summary>Persisted role revocation invalidates a reviewed confirmation despite a stale administrator cookie.</summary>
    [Fact]
    public async Task Commit_RejectsStaleAdministratorCookie_AfterRoleRevocation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var clubId = await AuthenticateAsync(client);
        var bytes = Csv();
        var preview = await PreviewAsync(client, bytes);
        await using (var db = fixture.CreateAdminContext())
        {
            var user = await db.Users.SingleAsync(candidate => candidate.ClubId == clubId, cancellationToken);
            var role = await db.Roles.SingleAsync(candidate => candidate.NormalizedName == Roles.ClubAdmin.ToUpperInvariant(), cancellationToken);
            var membership = await db.UserRoles.SingleAsync(candidate => candidate.UserId == user.Id && candidate.RoleId == role.Id, cancellationToken);
            db.UserRoles.Remove(membership);
            await db.SaveChangesAsync(cancellationToken);
        }
        using var form = Form(bytes, preview);
        using var response = await client.PostAsync(PlayerEndpoints.ImportCommit, form, cancellationToken);
        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
        await using var after = fixture.CreateAdminContext();
        (await after.Players.CountAsync(player => player.ClubId == clubId, cancellationToken)).ShouldBe(0);
        (await after.PlayerImportReceipts.CountAsync(receipt => receipt.ClubId == clubId, cancellationToken)).ShouldBe(0);
    }

    /// <summary>Authenticates a real user with a completed profile and persisted role membership.</summary>
    /// <param name="client">The cookie-preserving HTTP client.</param>
    /// <param name="administrator">Whether the user receives the club administrator role.</param>
    /// <returns>The isolated club identifier.</returns>
    private async Task<long> AuthenticateAsync(HttpClient client, bool administrator = true)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = $"import-commit-{Guid.CreateVersion7():N}@example.com";
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, "Test#Passw0rd!", cancellationToken);
        await using var db = fixture.CreateAdminContext();
        var user = await db.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        var club = new ClubEntity { Name = $"Import {Guid.CreateVersion7():N}", City = "Austin", State = "TX", CreationOperationId = Guid.CreateVersion7(), CreatedById = user.Id };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);
        user.FirstName = "Import";
        user.LastName = "Tester";
        user.ClubId = club.ClubId;
        if (administrator)
        {
            var role = await db.Roles.SingleAsync(candidate => candidate.NormalizedName == Roles.ClubAdmin.ToUpperInvariant(), cancellationToken);
            db.UserRoles.Add(new IdentityUserRole<long> { UserId = user.Id, RoleId = role.Id });
        }
        await db.SaveChangesAsync(cancellationToken);
        using var refresh = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/dashboard", cancellationToken);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Found);
        return club.ClubId;
    }

    /// <summary>Obtains a genuine opaque preview identity through the registered HTTP service.</summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <param name="bytes">The original CSV bytes.</param>
    /// <returns>The server preview.</returns>
    private static async Task<PlayerImportPreview> PreviewAsync(HttpClient client, byte[] bytes)
    {
        using var form = Form(bytes);
        using var response = await client.PostAsync(PlayerEndpoints.ImportPreview, form, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = await response.Content.ReadFromJsonAsync<PlayerImportPreview>(TestContext.Current.CancellationToken);
        preview.ShouldNotBeNull();
        return preview;
    }

    /// <summary>Builds a fresh multipart body for each attempt.</summary>
    /// <param name="bytes">The original file bytes.</param>
    /// <param name="preview">The optional confirmation fields.</param>
    /// <returns>A disposable multipart request body.</returns>
    private static MultipartFormDataContent Form(byte[] bytes, PlayerImportPreview? preview = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new("text/csv");
        form.Add(file, PlayerImportConstraints.FileFormFieldName, "players.csv");
        if (preview is not null)
        {
            form.Add(new StringContent(preview.OperationId.ToString()), PlayerImportConstraints.OperationIdFormFieldName);
            form.Add(new StringContent(preview.ConfirmationToken), PlayerImportConstraints.ConfirmationTokenFormFieldName);
        }
        return form;
    }

    /// <summary>Encodes valid UTF-8 CSV source rows.</summary>
    /// <param name="rows">The data rows.</param>
    /// <returns>The exact upload bytes.</returns>
    private static byte[] Csv(string rows = "Taylor,Stone,2013-02-03,,,2031\r\n") =>
        Encoding.UTF8.GetBytes(string.Join(',', PlayerImportConstraints.Headers) + "\r\n" + rows);

    /// <summary>Checks HTTP status and required ProblemDetails correlation.</summary>
    /// <param name="response">The failed HTTP response.</param>
    /// <param name="status">The expected status.</param>
    /// <returns>A task completing after body validation.</returns>
    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        response.StatusCode.ShouldBe(status);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)status);
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }
}
