using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Clubs;
using Nova.Shared.Enums;
using Nova.Shared.Players;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the player detail and campaign-history API endpoint.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class PlayerDetailHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies a same-club member can load player detail/history and receives campaign and trait projections.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetail_ReturnsPayload_ForCurrentClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAdminClient = fixture.CreateNovaHttpClient();
        using var clubMemberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("player-detail-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAdminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Pat", "Admin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(clubAdminClient, "Player Detail Club", "Austin", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAdminClient, cancellationToken);

        var memberEmail = UniqueEmail("player-detail-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubMemberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, "Morgan", "Member", club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(clubMemberClient, cancellationToken);

        var adminUserId = await GetUserIdByEmailAsync(adminEmail, cancellationToken);
        var playerId = await SeedPlayerHistoryAsync(club.ClubId, adminUserId, cancellationToken);

        using var response = await clubMemberClient.GetAsync(PlayerEndpoints.GetDetailUrl(playerId), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<PlayerDetailDto>(cancellationToken);
        payload.ShouldNotBeNull();
        payload.PlayerId.ShouldBe(playerId);
        payload.CampaignHistory.Count.ShouldBe(2);
        payload.CampaignHistory[0].CampaignStartDate.ShouldBeGreaterThanOrEqualTo(payload.CampaignHistory[1].CampaignStartDate);
        payload.CurrentTraits.Select(trait => trait.Name).ToList().ShouldBe(["Agility"]);
    }

    /// <summary>
    /// Verifies a club member from another tenant receives non-disclosing not-found behavior.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetail_ReturnsNotFound_ForCrossTenantClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var sourceClubAdminClient = fixture.CreateNovaHttpClient();
        using var otherClubAdminClient = fixture.CreateNovaHttpClient();

        var sourceAdminEmail = UniqueEmail("player-detail-source-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(sourceClubAdminClient, sourceAdminEmail, Password, cancellationToken);
        await UpdateUserAsync(sourceAdminEmail, "Sage", "Source", clubId: null, cancellationToken);
        var sourceClub = await CreateClubAsync(sourceClubAdminClient, "Source Club", "Dallas", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(sourceClubAdminClient, cancellationToken);

        var otherAdminEmail = UniqueEmail("player-detail-other-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClubAdminClient, otherAdminEmail, Password, cancellationToken);
        await UpdateUserAsync(otherAdminEmail, "Olive", "Other", clubId: null, cancellationToken);
        _ = await CreateClubAsync(otherClubAdminClient, "Other Club", "Houston", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClubAdminClient, cancellationToken);

        var sourceAdminUserId = await GetUserIdByEmailAsync(sourceAdminEmail, cancellationToken);
        var playerId = await SeedPlayerHistoryAsync(sourceClub.ClubId, sourceAdminUserId, cancellationToken);

        using var response = await otherClubAdminClient.GetAsync(PlayerEndpoints.GetDetailUrl(playerId), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies anonymous API requests receive an unauthorized response.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetail_ReturnsUnauthorized_ForAnonymousCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using var response = await anonymousClient.GetAsync(PlayerEndpoints.GetDetailUrl(123_456), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Seeds one player's campaign-history graph into the specified club and returns the player identifier.
    /// </summary>
    /// <param name="clubId">The target club identifier.</param>
    /// <param name="actorUserId">The user identifier stamped as the creator for seeded records.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded player identifier.</returns>
    private async Task<long> SeedPlayerHistoryAsync(long clubId, long actorUserId, CancellationToken cancellationToken)
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

        var player = new PlayerEntity
        {
            FirstName = "Drew",
            LastName = "Detail",
            DateOfBirth = new DateOnly(2011, 3, 15),
            GraduationYear = 2029,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Players.Add(player);

        var tag = new PlayerTagEntity
        {
            Name = "Agility",
            Color = "#0055AA",
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.PlayerTags.Add(tag);

        await context.SaveChangesAsync(cancellationToken);

        var activeAssignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = activeCampaign.CampaignId,
            TryoutNumber = 14,
            PlacementOutcome = PlacementOutcome.NotSelected,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        var closedAssignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = closedCampaign.CampaignId,
            TryoutNumber = 18,
            PlacementOutcome = PlacementOutcome.Withdrawn,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.PlayerCampaignAssignments.AddRange(activeAssignment, closedAssignment);
        await context.SaveChangesAsync(cancellationToken);

        context.Notes.Add(
            new NoteEntity
            {
                Content = "Solid footwork.",
                PlayerCampaignAssignmentId = activeAssignment.PlayerCampaignAssignmentId,
                ClubId = clubId,
                CreatedById = actorUserId
            });
        context.CampaignTagApplications.Add(
            new CampaignTagApplicationEntity
            {
                PlayerCampaignAssignmentId = activeAssignment.PlayerCampaignAssignmentId,
                PlayerTagId = tag.PlayerTagId,
                ClubId = clubId,
                CreatedById = actorUserId
            });

        await context.SaveChangesAsync(cancellationToken);
        return player.PlayerId;
    }

    /// <summary>
    /// Updates one seeded user's profile fields and optional club membership.
    /// </summary>
    /// <param name="email">The user email.</param>
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
    /// Gets the user identifier for the specified email.
    /// </summary>
    /// <param name="email">The user email.</param>
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
    /// <param name="client">The authenticated client.</param>
    /// <param name="name">The club name.</param>
    /// <param name="city">The club city.</param>
    /// <param name="state">The club state.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club DTO.</returns>
    private static async Task<ClubDto> CreateClubAsync(
        HttpClient client,
        string name,
        string city,
        string state,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(ClubEndpoints.Create, new CreateClubInput
        {
            Name = name,
            City = city,
            State = state
        }, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();
        return club;
    }

    /// <summary>
    /// Refreshes the authenticated cookie after club-membership changes.
    /// </summary>
    /// <param name="client">The authenticated client.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>
    /// Creates a unique email address for test-user registration.
    /// </summary>
    /// <param name="prefix">A scenario prefix for easier traceability.</param>
    /// <returns>A unique email address.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";
}
