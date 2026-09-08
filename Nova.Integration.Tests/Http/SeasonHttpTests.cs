using System.Net.Http.Json;
using System.Text.Json;
using Nova.Integration.Tests.Data;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Features.Seasons;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>Verifies first-class season creation, reads, and idempotent advancement over HTTP.</summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class SeasonHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>Verifies 201 locations resolve and same-operation advancement is replay-safe.</summary>
    [Fact]
#pragma warning disable MA0051 // Keep this complete setup, operation, and assertion sequence together as one regression scenario.
    public async Task SeasonLifecycleReturnsResolvableLocationsAndIdempotentAdvancementAsync()
#pragma warning restore MA0051
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(client, "season-lifecycle", cancellationToken);

        using var createResponse = await client.PostAsJsonAsync(
            SeasonEndpoints.GroupPrefix,
            new CreateSeasonInput
            {
                OperationId = Guid.NewGuid(),
                Name = "  First Season  ",
                StartDate = new DateOnly(2026, 1, 1)
            },
            cancellationToken);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        createResponse.Headers.Location.ShouldNotBeNull();
        var first = await createResponse.Content.ReadFromJsonAsync<SeasonSummary>(cancellationToken);
        first.ShouldNotBeNull();
        first.Name.ShouldBe("First Season");
        first.IsCurrent.ShouldBeTrue();

        using var detailResponse = await client.GetAsync(createResponse.Headers.Location, cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<SeasonDetailResult>(cancellationToken);
        detail.ShouldNotBeNull();
        detail.Season.SeasonId.ShouldBe(first.SeasonId);
        detail.Campaigns.ShouldBeEmpty();

        var operationId = Guid.NewGuid();
        var startInput = new StartNextSeasonInput
        {
            OperationId = operationId,
            ExpectedCurrentSeasonId = first.SeasonId,
            Name = "Next Season",
            StartDate = new DateOnly(2025, 12, 1)
        };
        using var firstStartResponse = await client.PostAsJsonAsync(
            SeasonEndpoints.StartNext,
            startInput,
            cancellationToken);
        firstStartResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        firstStartResponse.Headers.Location.ShouldNotBeNull();
        var advanced = await firstStartResponse.Content.ReadFromJsonAsync<StartNextSeasonResult>(cancellationToken);
        advanced.ShouldNotBeNull();
        advanced.PreviousSeasonId.ShouldBe(first.SeasonId);
        using var advancedDetailResponse = await client.GetAsync(
            firstStartResponse.Headers.Location,
            cancellationToken);
        advancedDetailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var advancedDetail = await advancedDetailResponse.Content
            .ReadFromJsonAsync<SeasonDetailResult>(cancellationToken);
        advancedDetail.ShouldNotBeNull();
        advancedDetail.Season.SeasonId.ShouldBe(advanced.CurrentSeason.SeasonId);

        using var repeatedResponse = await client.PostAsJsonAsync(
            SeasonEndpoints.StartNext,
            startInput,
            cancellationToken);
        repeatedResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var repeated = await repeatedResponse.Content.ReadFromJsonAsync<StartNextSeasonResult>(cancellationToken);
        repeated.ShouldNotBeNull();
        repeated.CurrentSeason.SeasonId.ShouldBe(advanced.CurrentSeason.SeasonId);

        using var staleResponse = await client.PostAsJsonAsync(
            SeasonEndpoints.StartNext,
            startInput with { OperationId = Guid.NewGuid(), Name = "Losing Race" },
            cancellationToken);
        staleResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await staleResponse.ToServiceProblemAsync(cancellationToken)).Kind
            .ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>Verifies reads require membership and writes require administration.</summary>
    [Fact]
    public async Task SeasonRoutesEnforceAnonymousMemberAndAdministratorPoliciesAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymous = fixture.CreateNovaHttpClient();
        using var anonymousRead = await anonymous.GetAsync(new Uri(SeasonEndpoints.GroupPrefix, UriKind.RelativeOrAbsolute), cancellationToken);
        anonymousRead.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var club = await RegisterClubAdminAsync(adminClient, "season-policy-admin", cancellationToken);
        await RegisterClubMemberAsync(memberClient, "season-policy-member", club.ClubId, cancellationToken);

        using var createdResponse = await adminClient.PostAsJsonAsync(
            SeasonEndpoints.GroupPrefix,
            ValidCreateInput("Member-readable season"),
            cancellationToken);
        createdResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createdResponse.Content.ReadFromJsonAsync<SeasonSummary>(cancellationToken);
        created.ShouldNotBeNull();

        using var memberRead = await memberClient.GetAsync(new Uri(SeasonEndpoints.GroupPrefix, UriKind.RelativeOrAbsolute), cancellationToken);
        memberRead.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var memberDetail = await memberClient.GetAsync(
new Uri(SeasonEndpoints.Detail(created.SeasonId), UriKind.RelativeOrAbsolute),
            cancellationToken);
        memberDetail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await memberDetail.Content.ReadFromJsonAsync<SeasonDetailResult>(cancellationToken);
        detail.ShouldNotBeNull();
        detail.Season.SeasonId.ShouldBe(created.SeasonId);
        detail.Season.Name.ShouldBe(created.Name);
        detail.Campaigns.ShouldBeEmpty();

        using var memberWrite = await memberClient.PostAsJsonAsync(
            SeasonEndpoints.GroupPrefix,
            ValidCreateInput("Member Attempt"),
            cancellationToken);
        memberWrite.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var advancementInput = ValidStartNextInput(created.SeasonId, "Unauthorized next season");
        using var memberAdvance = await memberClient.PostAsJsonAsync(
            SeasonEndpoints.StartNext,
            advancementInput,
            cancellationToken);
        memberAdvance.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var anonymousAdvance = await anonymous.PostAsJsonAsync(
            SeasonEndpoints.StartNext,
            advancementInput,
            cancellationToken);
        anonymousAdvance.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>Verifies invalid paging and cross-tenant detail reads use traced ProblemDetails.</summary>
    [Fact]
    public async Task SeasonQueriesReturnTracedProblemsForInvalidPagingAndCrossTenantDetailAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(clubAClient, "season-query-a", cancellationToken);
        _ = await RegisterClubAdminAsync(clubBClient, "season-query-b", cancellationToken);

        using var createdResponse = await clubAClient.PostAsJsonAsync(
            SeasonEndpoints.GroupPrefix,
            ValidCreateInput("Private Season"),
            cancellationToken);
        createdResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createdResponse.Content.ReadFromJsonAsync<SeasonSummary>(cancellationToken);
        created.ShouldNotBeNull();

        using var invalid = await clubAClient.GetAsync(
new Uri($"{SeasonEndpoints.GroupPrefix}?page=0&pageSize=51", UriKind.RelativeOrAbsolute),
            cancellationToken);
        await AssertProblemDetailsAsync(invalid, HttpStatusCode.BadRequest, cancellationToken);

        using var invalidDetail = await clubAClient.GetAsync(
new Uri($"{SeasonEndpoints.Detail(0)}?campaignPage=0&campaignPageSize=51", UriKind.RelativeOrAbsolute),
            cancellationToken);
        await AssertProblemDetailsAsync(invalidDetail, HttpStatusCode.BadRequest, cancellationToken);

        using var hidden = await clubBClient.GetAsync(
new Uri(SeasonEndpoints.Detail(created.SeasonId), UriKind.RelativeOrAbsolute),
            cancellationToken);
        await AssertProblemDetailsAsync(hidden, HttpStatusCode.NotFound, cancellationToken);
    }

    /// <summary>Verifies different operations racing from one expected current season have one winner.</summary>
    [Fact]
    public async Task ConcurrentAdvancementReturnsOneCreatedAndOneConflictAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(client, "season-race", cancellationToken);
        using var createdResponse = await client.PostAsJsonAsync(
            SeasonEndpoints.GroupPrefix,
            ValidCreateInput("Race Current"),
            cancellationToken);
        var current = await createdResponse.Content.ReadFromJsonAsync<SeasonSummary>(cancellationToken);
        current.ShouldNotBeNull();

        var firstTask = client.PostAsJsonAsync(
            SeasonEndpoints.StartNext,
            ValidStartNextInput(current.SeasonId, "Race A"),
            cancellationToken);
        var secondTask = client.PostAsJsonAsync(
            SeasonEndpoints.StartNext,
            ValidStartNextInput(current.SeasonId, "Race B"),
            cancellationToken);
        var responses = await Task.WhenAll(firstTask, secondTask);
        try
        {
            responses.Select(response => response.StatusCode)
                .Order()
                .ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict]);

            using var listResponse = await client.GetAsync(new Uri(SeasonEndpoints.GroupPrefix, UriKind.RelativeOrAbsolute), cancellationToken);
            var seasons = await listResponse.Content.ReadFromJsonAsync<SeasonPageResult>(cancellationToken);
            seasons.ShouldNotBeNull();
            seasons.Items.Count(season => season.IsCurrent).ShouldBe(1);
            seasons.TotalCount.ShouldBe(2);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>Verifies PostgreSQL rejects a metadata write that reuses an observed token.</summary>
    [Fact]
    public async Task UpdateSeasonReturnsConflictWhenConcurrencyTokenIsStaleAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(client, "season-concurrency", cancellationToken);
        using var createdResponse = await client.PostAsJsonAsync(
            SeasonEndpoints.GroupPrefix,
            ValidCreateInput("Concurrency Current"),
            cancellationToken);
        var created = await createdResponse.Content.ReadFromJsonAsync<SeasonSummary>(cancellationToken);
        created.ShouldNotBeNull();
        var input = new UpdateSeasonInput
        {
            ExpectedConcurrencyToken = created.ConcurrencyToken,
            Name = "First Update",
            StartDate = created.StartDate
        };

        using var first = await client.PutAsJsonAsync(
            SeasonEndpoints.Detail(created.SeasonId),
            input,
            cancellationToken);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var stale = await client.PutAsJsonAsync(
            SeasonEndpoints.Detail(created.SeasonId),
            input with { Name = "Stale Update" },
            cancellationToken);

        await AssertProblemDetailsAsync(stale, HttpStatusCode.Conflict, cancellationToken);
    }

    private static CreateSeasonInput ValidCreateInput(string name) => new()
    {
        OperationId = Guid.NewGuid(),
        Name = name,
        StartDate = new DateOnly(2026, 1, 1)
    };

    private static StartNextSeasonInput ValidStartNextInput(long currentSeasonId, string name) => new()
    {
        OperationId = Guid.NewGuid(),
        ExpectedCurrentSeasonId = currentSeasonId,
        Name = name,
        StartDate = new DateOnly(2027, 1, 1)
    };

    private async Task<ClubDto> RegisterClubAdminAsync(
        HttpClient client,
        string emailPrefix,
        CancellationToken cancellationToken)
    {
        var email = SeedingHelpers.UniqueEmail(emailPrefix);
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client,
            email,
            Password,
            cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, null, cancellationToken, "Season", "Admin");
        var club = await SeedingHelpers.CreateClubAsync(client, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
        return club;
    }

    private async Task RegisterClubMemberAsync(
        HttpClient client,
        string emailPrefix,
        long clubId,
        CancellationToken cancellationToken)
    {
        var email = SeedingHelpers.UniqueEmail(emailPrefix);
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client,
            email,
            Password,
            cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, clubId, cancellationToken, "Season", "Member");
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
    }

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        CancellationToken cancellationToken)
    {
        response.StatusCode.ShouldBe(expectedStatus);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)expectedStatus);
        document.RootElement.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        if (expectedStatus == HttpStatusCode.BadRequest)
        {
            document.RootElement.GetProperty("errors").ValueKind.ShouldBe(JsonValueKind.Object);
        }
        else if (document.RootElement.TryGetProperty("detail", out var detail))
        {
            detail.GetString().ShouldNotBeNullOrWhiteSpace();
        }

        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }
}
