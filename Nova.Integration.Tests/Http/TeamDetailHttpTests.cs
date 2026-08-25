using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Teams;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the team detail and placement-history API endpoint.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TeamDetailHttpTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// The shared test-account password used for all registered test users in this suite.
    /// </summary>
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies a same-club member can load team detail and receives placement-history projections.
    /// </summary>
    [Fact]
    public async Task GetTeamDetail_ReturnsPayload_ForCurrentClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAdminClient = fixture.CreateNovaHttpClient();
        using var clubMemberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("team-detail-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAdminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Jordan", "Admin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(clubAdminClient, "Team Detail Club", "Austin", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAdminClient, cancellationToken);

        var memberEmail = UniqueEmail("team-detail-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubMemberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, "Casey", "Member", club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(clubMemberClient, cancellationToken);

        var adminUserId = await GetUserIdByEmailAsync(adminEmail, cancellationToken);
        var teamId = await SeedTeamHistoryAsync(club.ClubId, adminUserId, cancellationToken);

        using var response = await clubMemberClient.GetAsync(TeamEndpoints.GetDetailUrl(teamId), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<TeamDetailDto>(cancellationToken);
        payload.ShouldNotBeNull();
        payload.TeamId.ShouldBe(teamId);
        payload.PlacementHistory.Count.ShouldBe(2);
        payload.PlacementHistory[0].CampaignStartDate.ShouldBeGreaterThanOrEqualTo(payload.PlacementHistory[1].CampaignStartDate);
        payload.ActivePlacementImpacts.Count.ShouldBe(1);
    }

    /// <summary>
    /// Verifies a club member from another tenant receives non-disclosing not-found behavior.
    /// </summary>
    [Fact]
    public async Task GetTeamDetail_ReturnsNotFound_ForCrossTenantClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var sourceClubAdminClient = fixture.CreateNovaHttpClient();
        using var otherClubAdminClient = fixture.CreateNovaHttpClient();

        var sourceAdminEmail = UniqueEmail("team-detail-source-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(sourceClubAdminClient, sourceAdminEmail, Password, cancellationToken);
        await UpdateUserAsync(sourceAdminEmail, "Riley", "Source", clubId: null, cancellationToken);
        var sourceClub = await CreateClubAsync(sourceClubAdminClient, "Source Team Club", "Dallas", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(sourceClubAdminClient, cancellationToken);

        var otherAdminEmail = UniqueEmail("team-detail-other-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClubAdminClient, otherAdminEmail, Password, cancellationToken);
        await UpdateUserAsync(otherAdminEmail, "Skyler", "Other", clubId: null, cancellationToken);
        _ = await CreateClubAsync(otherClubAdminClient, "Other Team Club", "Houston", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClubAdminClient, cancellationToken);

        var sourceAdminUserId = await GetUserIdByEmailAsync(sourceAdminEmail, cancellationToken);
        var teamId = await SeedTeamHistoryAsync(sourceClub.ClubId, sourceAdminUserId, cancellationToken);

        using var response = await otherClubAdminClient.GetAsync(TeamEndpoints.GetDetailUrl(teamId), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies anonymous API requests receive an unauthorized response.
    /// </summary>
    [Fact]
    public async Task GetTeamDetail_ReturnsUnauthorized_ForAnonymousCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using var response = await anonymousClient.GetAsync(TeamEndpoints.GetDetailUrl(123_456), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Seeds a team and two campaign placements (one active, one closed) into the specified club
    /// and returns the team identifier.
    /// </summary>
    /// <param name="clubId">The target club identifier.</param>
    /// <param name="actorUserId">The user identifier stamped as creator for seeded records.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded team identifier.</returns>
    private async Task<long> SeedTeamHistoryAsync(long clubId, long actorUserId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();

        var season = new SeasonEntity
        {
            Name = $"Season-{Guid.CreateVersion7():N}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Seasons.Add(season);
        await context.SaveChangesAsync(cancellationToken);

        var activeCampaign = new CampaignEntity
        {
            Name = "Active Campaign",
            StartDate = new DateOnly(2026, 9, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        var closedCampaign = new CampaignEntity
        {
            Name = "Closed Campaign",
            StartDate = new DateOnly(2026, 8, 1),
            Status = CampaignStatus.Closed,
            ClosedAt = DateTimeOffset.UtcNow.AddDays(-3),
            ClosedById = actorUserId,
            SeasonId = season.SeasonId,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Campaigns.AddRange(activeCampaign, closedCampaign);

        var team = new TeamEntity
        {
            Name = "U16 Blue",
            GraduationYear = 2028,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Teams.Add(team);

        var player = new PlayerEntity
        {
            FirstName = "Alex",
            LastName = "Detail",
            DateOfBirth = new DateOnly(2012, 5, 10),
            GraduationYear = 2030,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Players.Add(player);

        await context.SaveChangesAsync(cancellationToken);

        context.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity
            {
                PlayerId = player.PlayerId,
                CampaignId = activeCampaign.CampaignId,
                TeamId = team.TeamId,
                TryoutNumber = 5,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = clubId,
                CreatedById = actorUserId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerId = player.PlayerId,
                CampaignId = closedCampaign.CampaignId,
                TeamId = team.TeamId,
                TryoutNumber = 12,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = clubId,
                CreatedById = actorUserId
            });

        await context.SaveChangesAsync(cancellationToken);

        return team.TeamId;
    }

    /// <summary>
    /// Updates a seeded user's profile fields and optional club membership.
    /// </summary>
    /// <param name="email">The user email address.</param>
    /// <param name="firstName">The first name to set.</param>
    /// <param name="lastName">The last name to set.</param>
    /// <param name="clubId">The optional club membership value.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    private async Task UpdateUserAsync(
        string email,
        string firstName,
        string lastName,
        long? clubId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var normalizedEmail = email.ToUpperInvariant();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.ClubId = clubId;
        context.Users.Update(user);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the user identifier for the specified email address.
    /// </summary>
    /// <param name="email">The user email address.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The user identifier.</returns>
    private async Task<long> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var normalizedEmail = email.ToUpperInvariant();
        return await context.Users
            .Where(candidate => candidate.NormalizedEmail == normalizedEmail)
            .Select(candidate => candidate.Id)
            .SingleAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a club over HTTP for the authenticated user.
    /// </summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <param name="name">The club name.</param>
    /// <param name="city">The club city.</param>
    /// <param name="state">The club state abbreviation.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club DTO.</returns>
    private static async Task<ClubDto> CreateClubAsync(
        HttpClient client,
        string name,
        string city,
        string state,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            ClubEndpoints.Create,
            SeedingHelpers.CreateClubMultipartContent(name, city, state),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();
        return club;
    }

    /// <summary>
    /// Refreshes the authenticated cookie after club-membership changes.
    /// </summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/dashboard", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>
    /// Creates a unique email address for test-user registration.
    /// </summary>
    /// <param name="prefix">A scenario prefix for easier traceability.</param>
    /// <returns>A unique email address.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";
}
