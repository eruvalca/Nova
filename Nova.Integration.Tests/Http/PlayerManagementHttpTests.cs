using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Features.Players;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the player management API: create and update endpoints,
/// authorization, input validation, graduation-year blocking, and cross-tenant isolation.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed partial class PlayerManagementHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    // ── POST /api/players ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that an authenticated club admin can create a player and receives 201 Created
    /// with the new player's details in the response body.
    /// </summary>
    [Fact]
    public async Task CreateReturnsCreatedForClubAdminAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var email = UniqueEmail("create-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, "Pat", "PlayerAdmin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, "Test Club Create", "Austin", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.PostAsJsonAsync(PlayerEndpoints.Create, ValidCreateInput(club.ClubId), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<PlayerCreationCompletion>(cancellationToken);
        dto.ShouldNotBeNull();
        dto.Player.PlayerId.ShouldBeGreaterThan(0);
        dto.Player.FirstName.ShouldBe("Alex");
        dto.Player.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    /// <summary>
    /// Verifies that an approved club member can create
    /// a player.
    /// </summary>
    [Fact]
    public async Task CreateReturnsCreatedForClubMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("create-forbidden-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Sam", "Admin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, "Test Club Forbidden", "Denver", "CO", cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var memberEmail = UniqueEmail("create-forbidden-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, "Morgan", "Member", club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        using var response = await memberClient.PostAsJsonAsync(PlayerEndpoints.Create, ValidCreateInput(club.ClubId), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    /// <summary>
    /// Verifies that endpoint-layer validation rejects an invalid create-player payload with
    /// 400 Bad Request before the service executes.
    /// </summary>
    [Fact]
    public async Task CreateReturnsBadRequestForInvalidInputAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var email = UniqueEmail("create-invalid");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, "Dana", "Admin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, "Test Club Invalid", "Chicago", "IL", cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        var invalid = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = club.ClubId,
            FirstName = "",
            LastName = "",
            DateOfBirth = new DateOnly(2010, 1, 1),
            GraduationYear = 1999 // below allowed range
        };

        using var response = await client.PostAsJsonAsync(PlayerEndpoints.Create, invalid, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that an unauthenticated request to create a player returns 401 Unauthorized.
    /// </summary>
    [Fact]
    public async Task CreateReturnsUnauthorizedForAnonymousAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.PostAsJsonAsync(PlayerEndpoints.Create, ValidCreateInput(1), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ── PUT /api/players/{playerId} ────────────────────────────────────────────

    /// <summary>
    /// Verifies that an authenticated club admin can update a player's profile and receives 200 OK.
    /// </summary>
    [Fact]
    public async Task UpdateReturnsOkForClubAdminAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var email = UniqueEmail("update-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, "River", "Admin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, "Test Club Update", "Seattle", "WA", cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        var created = await CreatePlayerAsync(client, ValidCreateInput(club.ClubId), cancellationToken);

        var updateInput = new UpdatePlayerInput
        {
            PlayerId = created.PlayerId,
            FirstName = "UpdatedFirst",
            LastName = "UpdatedLast",
            DateOfBirth = new DateOnly(2011, 5, 15),
            GraduationYear = 2029
        };

        using var response = await client.PutAsJsonAsync(PlayerEndpoints.UpdateUrl(created.PlayerId), updateInput, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<PlayerDto>(cancellationToken);
        dto.ShouldNotBeNull();
        dto.FirstName.ShouldBe("UpdatedFirst");
        dto.GraduationYear.ShouldBe(2029);
    }

    /// <summary>
    /// Verifies that attempting to update a player belonging to another club returns 404 Not Found.
    /// </summary>
    [Fact]
    public async Task UpdateReturnsNotFoundForOtherClubPlayerAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminAClient = fixture.CreateNovaHttpClient();
        using var adminBClient = fixture.CreateNovaHttpClient();

        // Club A: register, create club, create player.
        var adminAEmail = UniqueEmail("update-xclub-admin-a");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminAClient, adminAEmail, Password, cancellationToken);
        await UpdateUserAsync(adminAEmail, "Alex", "AdminA", clubId: null, cancellationToken);
        var clubA = await CreateClubAsync(adminAClient, "Club A CrossTenant", "Portland", "OR", cancellationToken);
        await RefreshClubMembershipCookieAsync(adminAClient, cancellationToken);
        var playerA = await CreatePlayerAsync(adminAClient, ValidCreateInput(clubA.ClubId), cancellationToken);

        // Club B: register a different admin.
        var adminBEmail = UniqueEmail("update-xclub-admin-b");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminBClient, adminBEmail, Password, cancellationToken);
        await UpdateUserAsync(adminBEmail, "Brett", "AdminB", clubId: null, cancellationToken);
        _ = await CreateClubAsync(adminBClient, "Club B CrossTenant", "Miami", "FL", cancellationToken);
        await RefreshClubMembershipCookieAsync(adminBClient, cancellationToken);

        // Club B admin attempts to update Club A's player.
        var updateInput = new UpdatePlayerInput
        {
            PlayerId = playerA.PlayerId,
            FirstName = "CrossTenantAttack",
            LastName = "ByMember",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030
        };

        using var response = await adminBClient.PutAsJsonAsync(PlayerEndpoints.UpdateUrl(playerA.PlayerId), updateInput, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies that an approved club member can update
    /// a player in their own club.
    /// </summary>
    [Fact]
    public async Task UpdateReturnsOkForClubMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("update-forbidden-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Sam", "Admin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, "Update Forbidden Club", "Denver", "CO", cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);
        var player = await CreatePlayerAsync(adminClient, ValidCreateInput(club.ClubId), cancellationToken);

        var memberEmail = UniqueEmail("update-forbidden-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, "Morgan", "Member", club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        var updateInput = new UpdatePlayerInput
        {
            PlayerId = player.PlayerId,
            FirstName = "Corrected",
            LastName = "ByMember",
            DateOfBirth = new DateOnly(2012, 6, 15),
            GraduationYear = 2030
        };

        using var response = await memberClient.PutAsJsonAsync(PlayerEndpoints.UpdateUrl(player.PlayerId), updateInput, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that changing a player's graduation year to one that would make an Active Assigned
    /// placement ineligible returns 409 Conflict with structured blocker information.
    /// </summary>
    [Fact]
    public async Task UpdateReturnsConflictWithBlockersForIneligibleGraduationYearChangeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var email = UniqueEmail("update-blocked-gradyear");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, "Casey", "Admin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, "Blocker Test Club", "Nashville", "TN", cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        // Create a player with GraduationYear = 2030 so they're eligible.
        var player = await CreatePlayerAsync(client, ValidCreateInput(club.ClubId) with { GraduationYear = 2030 }, cancellationToken);

        // Directly seed an Assigned placement to a team with GraduationYear = 2031 via admin db context.
        // We need an Active campaign first — if there are none (no campaigns exist), skip or seed one.
        await SeedAssignedPlacementAsync(player.PlayerId, club.ClubId, teamGraduationYear: 2031, cancellationToken);

        // Now try to change the player's graduation year to 2028 < team's 2031 — should be blocked.
        var updateInput = new UpdatePlayerInput
        {
            PlayerId = player.PlayerId,
            FirstName = player.FirstName,
            LastName = player.LastName,
            DateOfBirth = new DateOnly(2013, 1, 1),
            GraduationYear = 2028
        };

        using var response = await client.PutAsJsonAsync(PlayerEndpoints.UpdateUrl(player.PlayerId), updateInput, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        doc.RootElement.GetProperty("errors").EnumerateObject()
            .Any(prop => prop.Name.StartsWith("blockers[", StringComparison.Ordinal))
            .ShouldBeTrue("conflict response should include structured blocker keys");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static CreatePlayerInput ValidCreateInput(long clubId) => new()
    {
        OperationId = Guid.CreateVersion7(),
        ClubId = clubId,
        FirstName = "Alex",
        LastName = "Player",
        DateOfBirth = new DateOnly(2012, 6, 15),
        GraduationYear = 2030
    };

    private static async Task<PlayerDto> CreatePlayerAsync(
        HttpClient client,
        CreatePlayerInput input,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(PlayerEndpoints.Create, input, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<PlayerCreationCompletion>(cancellationToken);
        dto.ShouldNotBeNull();
        return dto.Player;
    }

    /// <summary>
    /// Seeds a season, an Active campaign, a team with the specified graduation year,
    /// and an Assigned placement for the given player — all via the admin context.
    /// </summary>
    private async Task SeedAssignedPlacementAsync(
        long playerId,
        long clubId,
        int teamGraduationYear,
        CancellationToken cancellationToken)
    {
        var db = fixture.CreateAdminContext();
        await using (db)
        {
            var actorUserId = fixture.CurrentUser.UserId ?? 1L;

            var season = new Nova.Entities.SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Blocker Season {Guid.NewGuid():N}",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = clubId,
                CreatedById = actorUserId
            };
            db.Seasons.Add(season);
            await db.SaveChangesAsync(cancellationToken);

            var campaign = new Nova.Entities.CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Blocker Campaign {Guid.NewGuid():N}",
                StartDate = new DateOnly(2026, 6, 1),
                Status = Nova.SharedKernel.Enums.CampaignStatus.Active,
                SeasonId = season.SeasonId,
                ClubId = clubId,
                CreatedById = actorUserId
            };
            db.Campaigns.Add(campaign);

            var team = new Nova.Entities.TeamEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Blocker Team {teamGraduationYear}",
                GraduationYear = teamGraduationYear,
                ClubId = clubId,
                CreatedById = actorUserId
            };
            db.Teams.Add(team);
            await db.SaveChangesAsync(cancellationToken);

            db.PlayerCampaignAssignments.Add(new Nova.Entities.PlayerCampaignAssignmentEntity
            {
                PlayerId = playerId,
                CampaignId = campaign.CampaignId,
                TeamId = team.TeamId,
                PlacementOutcome = Nova.SharedKernel.Enums.PlacementOutcome.Assigned,
                ClubId = clubId,
                CreatedById = actorUserId
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<ClubDto> CreateClubAsync(
        HttpClient client,
        string name,
        string city,
        string state,
        CancellationToken cancellationToken)
    {
        using var responseRequestContent = SeedingHelpers.CreateClubMultipartContent(name, city, state);
        using var response = await client.PostAsync(
        new Uri(ClubEndpoints.Create, UriKind.RelativeOrAbsolute),
                    responseRequestContent,
                    cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();
        return club;
    }

    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"{ClubEndpoints.Complete}?returnUrl=/dashboard", UriKind.RelativeOrAbsolute), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    private async Task UpdateUserAsync(
        string email,
        string firstName,
        string lastName,
        long? clubId,
        CancellationToken cancellationToken)
    {
        var context = fixture.CreateAdminContext();
        await using (context)
        {
            var normalizedEmail = email.ToUpperInvariant();
            var user = await context.Users.SingleAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
            user.FirstName = firstName;
            user.LastName = lastName;
            user.ClubId = clubId;
            context.Users.Update(user);
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";
}
