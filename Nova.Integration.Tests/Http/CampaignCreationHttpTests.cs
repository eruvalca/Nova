using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Campaigns;
using Nova.Shared.Clubs;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for campaign creation authorization, validation, tenancy, conflicts,
/// and successful response serialization.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignCreationHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies the campaign creation route is registered and rejects anonymous callers.
    /// </summary>
    [Fact]
    public async Task CreateCampaign_ReturnsUnauthorized_ForAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.PostAsJsonAsync(
            CampaignEndpoints.Create,
            ValidInlineInput(),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies an authenticated non-administrator club member cannot create a campaign.
    /// </summary>
    [Fact]
    public async Task CreateCampaign_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "campaign-forbidden-admin", cancellationToken);

        var memberEmail = UniqueEmail("campaign-forbidden-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            memberClient,
            memberEmail,
            Password,
            cancellationToken);
        await UpdateUserAsync(memberEmail, "Campaign", "Member", admin.Club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        using var response = await memberClient.PostAsJsonAsync(
            CampaignEndpoints.Create,
            ValidInlineInput(),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies endpoint validation returns structured validation ProblemDetails.
    /// </summary>
    [Fact]
    public async Task CreateCampaign_ReturnsValidationProblem_ForInvalidInput()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(client, "campaign-validation-admin", cancellationToken);
        var invalid = ValidInlineInput() with { OperationId = Guid.Empty };

        using var response = await client.PostAsJsonAsync(
            CampaignEndpoints.Create,
            invalid,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("errors")
            .TryGetProperty(nameof(CreateCampaignInput.OperationId), out _)
            .ShouldBeTrue();
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies another club's season is hidden as not found.
    /// </summary>
    [Fact]
    public async Task CreateCampaign_ReturnsNotFound_ForCrossTenantSeason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();
        var clubA = await RegisterClubAdminAsync(clubAClient, "campaign-cross-a", cancellationToken);
        var season = await SeedSeasonAsync(clubA, cancellationToken);
        _ = await RegisterClubAdminAsync(clubBClient, "campaign-cross-b", cancellationToken);

        using var response = await clubBClient.PostAsJsonAsync(
            CampaignEndpoints.Create,
            ValidExistingSeasonInput(season.SeasonId),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await AssertProblemDetailsAsync(response, (int)HttpStatusCode.NotFound, cancellationToken);
    }

    /// <summary>
    /// Verifies duplicate names within one season map to conflict ProblemDetails.
    /// </summary>
    [Fact]
    public async Task CreateCampaign_ReturnsConflict_ForDuplicateNameWithinSeason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(client, "campaign-conflict-admin", cancellationToken);
        var season = await SeedSeasonAsync(admin, cancellationToken);
        var campaignName = $"Duplicate Campaign {Guid.CreateVersion7():N}";
        var firstInput = ValidExistingSeasonInput(season.SeasonId) with { Name = campaignName };

        using (var first = await client.PostAsJsonAsync(
            CampaignEndpoints.Create,
            firstInput,
            cancellationToken))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var duplicate = await client.PostAsJsonAsync(
            CampaignEndpoints.Create,
            firstInput with { OperationId = Guid.CreateVersion7() },
            cancellationToken);

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await AssertProblemDetailsAsync(duplicate, (int)HttpStatusCode.Conflict, cancellationToken);
    }

    /// <summary>
    /// Verifies successful inline-season creation returns the complete committed aggregate.
    /// </summary>
    [Fact]
    public async Task CreateCampaign_ReturnsCreatedAggregate_ForInlineSeason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(client, "campaign-success-admin", cancellationToken);
        var input = ValidInlineInput();

        using var response = await client.PostAsJsonAsync(
            CampaignEndpoints.Create,
            input,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldBeNull();
        var created = await response.Content.ReadFromJsonAsync<CreateCampaignResult>(cancellationToken);
        created.ShouldNotBeNull();
        created.OperationId.ShouldBe(input.OperationId);
        created.CampaignId.ShouldBeGreaterThan(0);
        created.CampaignName.ShouldBe(input.Name);
        created.CampaignStartDate.ShouldBe(input.StartDate);
        created.CampaignPlannedEndDate.ShouldBe(input.PlannedEndDate);
        created.Status.ShouldBe(CampaignStatus.Active);
        created.SeasonId.ShouldBeGreaterThan(0);
        created.SeasonName.ShouldBe(input.InlineSeason!.Name);
        created.SeasonStartDate.ShouldBe(input.InlineSeason.StartDate);
        created.SeasonEndDate.ShouldBe(input.InlineSeason.EndDate);
        created.SeasonCreatedInline.ShouldBeTrue();
        created.EnrolledPlayerCount.ShouldBe(0);
    }

    private static CreateCampaignInput ValidInlineInput() => new()
    {
        OperationId = Guid.CreateVersion7(),
        Name = $"Campaign {Guid.CreateVersion7():N}",
        StartDate = new DateOnly(2026, 6, 1),
        PlannedEndDate = new DateOnly(2026, 6, 30),
        InlineSeason = new InlineSeasonInput
        {
            Name = $"Season {Guid.CreateVersion7():N}",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31)
        }
    };

    private static CreateCampaignInput ValidExistingSeasonInput(long seasonId) => new()
    {
        OperationId = Guid.CreateVersion7(),
        Name = $"Campaign {Guid.CreateVersion7():N}",
        StartDate = new DateOnly(2026, 6, 1),
        PlannedEndDate = new DateOnly(2026, 6, 30),
        ExistingSeasonId = seasonId
    };

    private async Task<CampaignHttpActor> RegisterClubAdminAsync(
        HttpClient client,
        string emailPrefix,
        CancellationToken cancellationToken)
    {
        var email = UniqueEmail(emailPrefix);
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client,
            email,
            Password,
            cancellationToken);
        var userId = await UpdateUserAsync(email, "Campaign", "Admin", clubId: null, cancellationToken);

        using var response = await client.PostAsJsonAsync(
            ClubEndpoints.Create,
            new CreateClubInput
            {
                Name = $"Campaign Club {Guid.CreateVersion7():N}",
                City = "Austin",
                State = "TX"
            },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        return new CampaignHttpActor(userId, club);
    }

    private async Task<SeasonEntity> SeedSeasonAsync(
        CampaignHttpActor actor,
        CancellationToken cancellationToken)
    {
        fixture.CurrentUser.UserId = actor.UserId;
        fixture.CurrentUser.ClubId = actor.Club.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;

        await using var context = fixture.CreateAdminContext();
        var season = new SeasonEntity
        {
            Name = $"HTTP Season {Guid.CreateVersion7():N}",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            ClubId = actor.Club.ClubId,
            CreatedById = actor.UserId
        };
        context.Seasons.Add(season);
        await context.SaveChangesAsync(cancellationToken);
        return season;
    }

    private async Task<long> UpdateUserAsync(
        string email,
        string firstName,
        string lastName,
        long? clubId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var normalizedEmail = email.ToUpperInvariant();
        var user = await context.Users.SingleAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail,
            cancellationToken);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.ClubId = clubId;
        context.Users.Update(user);
        await context.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    private static async Task RefreshClubMembershipCookieAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"{ClubEndpoints.Complete}?returnUrl=/",
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        int expectedStatus,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe(expectedStatus);
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    private static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    private sealed record CampaignHttpActor(long UserId, ClubDto Club);
}
