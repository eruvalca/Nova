using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Players;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>End-to-end HTTP coverage for administrator-only player import template and preview.</summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class PlayerImportHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Fact]
    public async Task Template_ReturnsUnauthorized_ForAnonymousCaller()
    {
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.GetAsync(
            PlayerEndpoints.ImportTemplate,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Preview_ReturnsUnauthorized_ForAnonymousCaller()
    {
        using var client = fixture.CreateNovaHttpClient();
        using var form = CsvForm("Alex,Archer,2012-01-01,,,2030\r\n");

        using var response = await client.PostAsync(
            PlayerEndpoints.ImportPreview,
            form,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Template_ReturnsExactCsvDownload_ForClubAdministrator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await CreateAdministratorClubAsync(client, "template", cancellationToken);

        using var response = await client.GetAsync(PlayerEndpoints.ImportTemplate, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        response.Content.Headers.ContentType.CharSet.ShouldBe("utf-8");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe(PlayerImportConstraints.TemplateFileName);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        bytes.Take(3).ShouldBe([0xEF, 0xBB, 0xBF]);
        Encoding.UTF8.GetString(bytes[3..]).ShouldBe(
            "First name,Last name,Date of birth,Gender,Jersey number,Graduation year\r\n");
    }

    [Fact]
    public async Task Preview_ReturnsForbidden_ForOrdinaryClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var club = await CreateAdministratorClubAsync(adminClient, "member-auth", cancellationToken);
        await CreateClubMemberAsync(memberClient, club.ClubId, "member-auth", cancellationToken);
        using var form = CsvForm("Alex,Archer,2012-01-01,,,2030\r\n");

        using var response = await memberClient.PostAsync(PlayerEndpoints.ImportPreview, form, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Template_ReturnsForbidden_ForOrdinaryClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var club = await CreateAdministratorClubAsync(adminClient, "template-member-auth", cancellationToken);
        await CreateClubMemberAsync(memberClient, club.ClubId, "template-member-auth", cancellationToken);

        using var response = await memberClient.GetAsync(PlayerEndpoints.ImportTemplate, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Preview_ReturnsValidationProblemWithTraceId_WhenFileIsMissing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await CreateAdministratorClubAsync(client, "missing-file", cancellationToken);
        using var form = new MultipartFormDataContent();

        using var response = await client.PostAsync(PlayerEndpoints.ImportPreview, form, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe(400);
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Preview_ClassifiesRowsAndDoesNotPersistAnything_ForClubAdministrator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var club = await CreateAdministratorClubAsync(client, "preview", cancellationToken);
        var existing = await CreatePlayerAsync(client, cancellationToken);
        var before = await CountsAsync(club.ClubId, cancellationToken);
        using var form = CsvForm(
            " Alex ,ARCHER,2012-01-01,,,2030\r\n"
            + "Taylor,Stone,2013-02-03,,,2031\r\n"
            + "taylor,stone,2013-02-03,,,2031\r\n"
            + "=bad,Player,not-a-date,,,1999\r\n");

        using var response = await client.PostAsync(PlayerEndpoints.ImportPreview, form, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = await response.Content.ReadFromJsonAsync<PlayerImportPreview>(cancellationToken);
        preview.ShouldNotBeNull();
        preview.TotalRows.ShouldBe(4);
        preview.ReadyRows.ShouldBe(1);
        preview.InvalidRows.ShouldBe(1);
        preview.DuplicateRows.ShouldBe(2);
        preview.Rows[0].Duplicate!.ExistingPlayerId.ShouldBe(existing.PlayerId);
        preview.Rows[2].Duplicate!.EarlierSourceRowNumber.ShouldBe(3);
        preview.OperationId.ShouldNotBe(Guid.Empty);
        preview.ConfirmationToken.ShouldNotBeNullOrWhiteSpace();
        preview.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        (await CountsAsync(club.ClubId, cancellationToken)).ShouldBe(before);
    }

    [Fact]
    public async Task Preview_AcceptsMaximumRowCount_OverRealMultipartBoundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await CreateAdministratorClubAsync(client, "maximum", cancellationToken);
        var rows = string.Join(
            "\r\n",
            Enumerable.Range(1, PlayerImportConstraints.MaxDataRows)
                .Select(index => $"Player{index},Maximum,2012-01-01,,,{2000 + index % 100}")) + "\r\n";
        using var form = CsvForm(rows);

        using var response = await client.PostAsync(PlayerEndpoints.ImportPreview, form, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var preview = await response.Content.ReadFromJsonAsync<PlayerImportPreview>(cancellationToken);
        preview.ShouldNotBeNull();
        preview.TotalRows.ShouldBe(PlayerImportConstraints.MaxDataRows);
        preview.ReadyRows.ShouldBe(PlayerImportConstraints.MaxDataRows);
    }

    [Fact]
    public async Task Preview_RejectsInvalidFileBoundaries_WithTraceCorrelatedProblemDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await CreateAdministratorClubAsync(client, "invalid-files", cancellationToken);
        var header = Encoding.UTF8.GetBytes(
            "First name,Last name,Date of birth,Gender,Jersey number,Graduation year\r\n");
        var overRows = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(header)
            + string.Concat(Enumerable.Repeat("Alex,Archer,2012-01-01,,,2030\r\n", 1_001)));
        (byte[] Content, string FileName, string ContentType)[] cases =
        [
            ([], "players.csv", "text/csv"),
            (header, "players.txt", "text/csv"),
            (header, "players.csv", "image/png"),
            ([.. header, 0xC3, 0x28], "players.csv", "text/csv"),
            (header, "players.csv", "text/csv"),
            (Encoding.UTF8.GetBytes("Wrong,Headers\r\nvalue,value\r\n"), "players.csv", "text/csv"),
            (Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(header) + "\"unterminated"), "players.csv", "text/csv"),
            (overRows, "players.csv", "text/csv"),
            (new byte[PlayerImportConstraints.MaxFileBytes + 1], "players.csv", "text/csv")
        ];

        foreach (var testCase in cases)
        {
            using var form = CsvForm(testCase.Content, testCase.FileName, testCase.ContentType);
            using var response = await client.PostAsync(PlayerEndpoints.ImportPreview, form, cancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, testCase.FileName);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        }
    }

    private static MultipartFormDataContent CsvForm(string rows)
    {
        const string header = "First name,Last name,Date of birth,Gender,Jersey number,Graduation year\r\n";
        return CsvForm(Encoding.UTF8.GetBytes(header + rows), "players.csv", "text/csv");
    }

    private static MultipartFormDataContent CsvForm(byte[] content, string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new(contentType);
        form.Add(file, PlayerImportConstraints.FileFormFieldName, fileName);
        return form;
    }

    private static async Task<PlayerDto> CreatePlayerAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(PlayerEndpoints.Create, new CreatePlayerInput
        {
            FirstName = "Alex",
            LastName = "Archer",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030
        }, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PlayerDto>(cancellationToken))!;
    }

    private async Task<(int Players, int Assignments)> CountsAsync(long clubId, CancellationToken cancellationToken)
    {
        await using var db = fixture.CreateAdminContext();
        return (
            await db.Players.CountAsync(player => player.ClubId == clubId, cancellationToken),
            await db.PlayerCampaignAssignments.CountAsync(assignment => assignment.ClubId == clubId, cancellationToken));
    }

    private async Task<ClubDto> CreateAdministratorClubAsync(
        HttpClient client,
        string prefix,
        CancellationToken cancellationToken)
    {
        var email = UniqueEmail(prefix + "-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        using var response = await client.PostAsync(
            ClubEndpoints.Create,
            SeedingHelpers.CreateClubMultipartContent($"{prefix} {Guid.NewGuid():N}", "Austin", "TX"),
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        return club;
    }

    private async Task CreateClubMemberAsync(
        HttpClient client,
        long clubId,
        string prefix,
        CancellationToken cancellationToken)
    {
        var email = UniqueEmail(prefix + "-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
    }

    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
    {
        await using var db = fixture.CreateAdminContext();
        var normalizedEmail = email.ToUpperInvariant();
        var user = await db.Users.SingleAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
        user.FirstName = "Import";
        user.LastName = "Tester";
        user.ClubId = clubId;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task RefreshClubMembershipCookieAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/dashboard", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";
}
