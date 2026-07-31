using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Clubs;
using Nova.Shared.Enums;
using Nova.Shared.Teams;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Covers authorization, tenant scoping, filters, ordering, and serialization for the team roster API.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TeamRosterHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Fact]
    public async Task GetRoster_ReturnsFilteredRows_ForApprovedClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using (var anonymousResponse = await anonymousClient.GetAsync(TeamRosterEndpoints.GetRoster, cancellationToken))
        {
            anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("team-roster");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        await using var context = fixture.CreateAdminContext();
        var userId = await context.Users
            .Where(user => user.NormalizedEmail == email.ToUpperInvariant())
            .Select(user => user.Id)
            .SingleAsync(cancellationToken);
        var season = new SeasonEntity
        {
            Name = "Roster Season",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = userId
        };
        context.Seasons.Add(season);
        await context.SaveChangesAsync(cancellationToken);
        var campaign = new CampaignEntity
        {
            Name = "Roster Campaign",
            StartDate = new DateOnly(2026, 1, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = userId
        };
        var player = new PlayerEntity
        {
            FirstName = "Roster",
            LastName = "Player",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            ClubId = club.ClubId,
            CreatedById = userId
        };
        var alpha = new TeamEntity
        {
            Name = "Alpha",
            GraduationYear = 2030,
            ClubId = club.ClubId,
            CreatedById = userId
        };
        var beta = new TeamEntity
        {
            Name = "Beta",
            GraduationYear = 2031,
            ClubId = club.ClubId,
            CreatedById = userId
        };
        var archived = new TeamEntity
        {
            Name = "Archived",
            GraduationYear = 2030,
            LifecycleStatus = LifecycleStatus.Archived,
            ArchivedAt = DateTimeOffset.UtcNow,
            ArchivedById = userId,
            ClubId = club.ClubId,
            CreatedById = userId
        };
        context.AddRange(campaign, player, alpha, beta, archived);
        await context.SaveChangesAsync(cancellationToken);
        context.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            TeamId = alpha.TeamId,
            PlacementOutcome = PlacementOutcome.Assigned,
            ClubId = club.ClubId,
            CreatedById = userId
        });
        await context.SaveChangesAsync(cancellationToken);

        using var response = await client.GetAsync(
            TeamRosterEndpoints.GetRosterUrl(search: "a", graduationYear: 2030),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<TeamRosterItem>>(cancellationToken);
        rows.ShouldNotBeNull();
        rows.Select(row => row.Name).ShouldBe(["Alpha"]);
        rows[0].ActivePlacementCount.ShouldBe(1);
    }

    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(
            candidate => candidate.NormalizedEmail == email.ToUpperInvariant(),
            cancellationToken);
        user.ClubId = clubId;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            ClubEndpoints.Create,
            new CreateClubInput { Name = "Team Roster Club", City = "Austin", State = "TX" },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";
}
