using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for team archive/restore lifecycle endpoints, including authorization,
/// blocker payload contracts, and cross-club tenant isolation.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TeamLifecycleHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies both lifecycle endpoints reject anonymous callers.
    /// </summary>
    /// <param name="operation">The lifecycle operation under test.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("archive")]
    [InlineData("restore")]
    public async Task TeamLifecycleEndpoints_ReturnUnauthorized_ForAnonymous(string operation)
    {
        using var client = fixture.CreateNovaHttpClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var teamId = 999_999L;

        using var response = await client.PostAsync(
            operation == "archive"
                ? TeamEndpoints.ArchiveUrl(teamId)
                : TeamEndpoints.RestoreUrl(teamId),
            content: null,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies a club administrator can archive and then restore a team over HTTP.
    /// </summary>
    [Fact]
    public async Task TeamLifecycleEndpoints_ArchiveRestoreRoundTrip_ReturnsNoContentAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(adminClient, "team-lifecycle-roundtrip-admin", "Roundtrip Rovers", cancellationToken);
        var teamId = await SeedTeamAsync(club.ClubId, cancellationToken);

        using (var archive = await adminClient.PostAsync(TeamEndpoints.ArchiveUrl(teamId), content: null, cancellationToken))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await using (var afterArchive = fixture.CreateAdminContext())
        {
            var archived = await afterArchive.Teams.SingleAsync(team => team.TeamId == teamId, cancellationToken);
            archived.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
            archived.ArchivedAt.ShouldNotBeNull();
        }

        using (var restore = await adminClient.PostAsync(TeamEndpoints.RestoreUrl(teamId), content: null, cancellationToken))
        {
            restore.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await using var verify = fixture.CreateAdminContext();
        var team = await verify.Teams.SingleAsync(candidate => candidate.TeamId == teamId, cancellationToken);
        team.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        team.ArchivedAt.ShouldBeNull();
        team.ArchivedById.ShouldBeNull();
    }

    /// <summary>
    /// Verifies archiving a team holding active-campaign placements returns a conflict carrying
    /// structured blockers in the problem extensions.
    /// </summary>
    [Fact]
    public async Task ArchiveEndpoint_ReturnsStructuredBlockers_ForActiveCampaignPlacementsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(adminClient, "team-lifecycle-blockers-admin", "Blocker Bandits", cancellationToken);
        var (teamId, campaignId, placementId) = await SeedBlockedTeamAsync(club.ClubId, cancellationToken);

        using var archive = await adminClient.PostAsync(TeamEndpoints.ArchiveUrl(teamId), content: null, cancellationToken);
        archive.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var problem = await archive.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        TeamLifecycleProblemExtensions.TryGetArchiveBlockers(problem, out var blockers).ShouldBeTrue();
        blockers.Count.ShouldBe(1);
        blockers[0].CampaignId.ShouldBe(campaignId);
        blockers[0].CampaignName.ShouldBe("Active Team Blocker Campaign");
        blockers[0].PlacementIds.ShouldBe([placementId]);

        await using var verify = fixture.CreateAdminContext();
        var team = await verify.Teams.SingleAsync(candidate => candidate.TeamId == teamId, cancellationToken);
        team.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
    }

    /// <summary>
    /// Verifies a non-administrator club member is forbidden, and that a team owned by another club
    /// is reported as missing rather than forbidden so identifiers are not disclosed.
    /// </summary>
    [Fact]
    public async Task ArchiveEndpoint_ReturnsForbiddenForNonAdmin_AndNotFoundForCrossTenantAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAAdminClient = fixture.CreateNovaHttpClient();
        using var clubAMemberClient = fixture.CreateNovaHttpClient();
        using var clubBAdminClient = fixture.CreateNovaHttpClient();

        var clubA = await RegisterClubAdminAsync(clubAAdminClient, "team-lifecycle-cluba-admin", "Alpha Team Club", cancellationToken);
        var clubB = await RegisterClubAdminAsync(clubBAdminClient, "team-lifecycle-clubb-admin", "Bravo Team Club", cancellationToken);

        var memberEmail = UniqueEmail("team-lifecycle-cluba-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubAMemberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, "Morgan", "ClubAMember", clubA.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(clubAMemberClient, cancellationToken);

        var clubATeamId = await SeedTeamAsync(clubA.ClubId, cancellationToken);
        var clubBTeamId = await SeedTeamAsync(clubB.ClubId, cancellationToken);

        using (var forbidden = await clubAMemberClient.PostAsync(TeamEndpoints.ArchiveUrl(clubATeamId), content: null, cancellationToken))
        {
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var notFound = await clubAAdminClient.PostAsync(TeamEndpoints.ArchiveUrl(clubBTeamId), content: null, cancellationToken))
        {
            notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using var restoreNotFound = await clubAAdminClient.PostAsync(TeamEndpoints.RestoreUrl(clubBTeamId), content: null, cancellationToken);
        restoreNotFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies a club administrator cannot read or update a team owned by another club, and that a
    /// club roster never leaks another club's teams.
    /// </summary>
    [Fact]
    public async Task TeamEndpoints_IsolateTenants_ForDetailUpdateAndRosterAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAAdminClient = fixture.CreateNovaHttpClient();
        using var clubBAdminClient = fixture.CreateNovaHttpClient();

        var clubA = await RegisterClubAdminAsync(clubAAdminClient, "team-crosstenant-cluba-admin", "Alpha Isolation Club", cancellationToken);
        var clubB = await RegisterClubAdminAsync(clubBAdminClient, "team-crosstenant-clubb-admin", "Bravo Isolation Club", cancellationToken);

        var clubATeamId = await SeedTeamAsync(clubA.ClubId, cancellationToken);
        var clubBTeamId = await SeedTeamAsync(clubB.ClubId, cancellationToken);

        using (var detail = await clubAAdminClient.GetAsync(TeamEndpoints.GetDetailUrl(clubBTeamId), cancellationToken))
        {
            detail.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var update = await clubAAdminClient.PutAsJsonAsync(
            TeamEndpoints.UpdateUrl(clubBTeamId),
            new UpdateTeamInput { TeamId = clubBTeamId, Name = "Hijacked", GraduationYear = 2031 },
            cancellationToken))
        {
            update.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var roster = await clubAAdminClient.GetAsync(TeamRosterEndpoints.GetRoster, cancellationToken))
        {
            roster.StatusCode.ShouldBe(HttpStatusCode.OK);
            var rows = await roster.Content.ReadFromJsonAsync<List<TeamRosterItem>>(cancellationToken);
            rows.ShouldNotBeNull();
            rows.Select(row => row.TeamId).ShouldContain(clubATeamId);
            rows.Select(row => row.TeamId).ShouldNotContain(clubBTeamId);
        }

        await using var verify = fixture.CreateAdminContext();
        var team = await verify.Teams.SingleAsync(candidate => candidate.TeamId == clubBTeamId, cancellationToken);
        team.Name.ShouldNotBe("Hijacked");
    }

    /// <summary>
    /// Registers a new user, creates a club for them, and refreshes their membership claims so they
    /// act as that club's administrator.
    /// </summary>
    /// <param name="client">The caller client that will hold the authentication cookie.</param>
    /// <param name="emailPrefix">A human-readable scenario prefix for the generated email.</param>
    /// <param name="clubName">The club name to create.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club DTO.</returns>
    private async Task<ClubDto> RegisterClubAdminAsync(
        HttpClient client,
        string emailPrefix,
        string clubName,
        CancellationToken cancellationToken)
    {
        var email = UniqueEmail(emailPrefix);
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, "Club", "Admin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, $"{clubName} {Guid.CreateVersion7():N}", cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        return club;
    }

    /// <summary>
    /// Creates a club over HTTP for the authenticated caller.
    /// </summary>
    /// <param name="client">The caller client.</param>
    /// <param name="name">The club name.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club DTO.</returns>
    private static async Task<ClubDto> CreateClubAsync(
        HttpClient client,
        string name,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            ClubEndpoints.Create,
            SeedingHelpers.CreateClubMultipartContent(name, "Austin", "TX"),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();
        return club;
    }

    /// <summary>
    /// Refreshes claims in the authentication cookie after a club membership mutation.
    /// </summary>
    /// <param name="client">The authenticated client.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes once refresh is confirmed.</returns>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/dashboard", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>
    /// Updates seeded Identity user names and optional club membership using the admin context.
    /// </summary>
    /// <param name="email">The user email to update.</param>
    /// <param name="firstName">The first name.</param>
    /// <param name="lastName">The last name.</param>
    /// <param name="clubId">The optional club assignment.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when persisted.</returns>
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
    /// Seeds one active team with no placements.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded team identifier.</returns>
    private async Task<long> SeedTeamAsync(long clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var actorUserId = await context.Users
            .Where(user => user.ClubId == clubId)
            .Select(user => user.Id)
            .FirstAsync(cancellationToken);

        var team = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Team-{Guid.CreateVersion7():N}",
            GraduationYear = 2030,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Teams.Add(team);
        await context.SaveChangesAsync(cancellationToken);
        return team.TeamId;
    }

    /// <summary>
    /// Seeds a team blocked from archive by one placement in an active campaign.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The identifiers used by blocker assertions.</returns>
    private async Task<(long TeamId, long CampaignId, long PlacementId)> SeedBlockedTeamAsync(
        long clubId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var actorUserId = await context.Users
            .Where(user => user.ClubId == clubId)
            .Select(user => user.Id)
            .FirstAsync(cancellationToken);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Season-{Guid.CreateVersion7():N}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Seasons.Add(season);
        await context.SaveChangesAsync(cancellationToken);

        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Active Team Blocker Campaign",
            StartDate = new DateOnly(2026, 8, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Campaigns.Add(campaign);

        var team = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Team-{Guid.CreateVersion7():N}",
            GraduationYear = 2030,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Teams.Add(team);

        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Placed",
            LastName = "Player",
            DateOfBirth = new DateOnly(2012, 3, 3),
            GraduationYear = 2030,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.Players.Add(player);
        await context.SaveChangesAsync(cancellationToken);

        var placement = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            TeamId = team.TeamId,
            PlacementOutcome = PlacementOutcome.Assigned,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        context.PlayerCampaignAssignments.Add(placement);
        await context.SaveChangesAsync(cancellationToken);

        return (team.TeamId, campaign.CampaignId, placement.PlayerCampaignAssignmentId);
    }

    /// <summary>
    /// Creates a unique test email.
    /// </summary>
    /// <param name="prefix">A human-readable scenario prefix.</param>
    /// <returns>A unique email value.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";
}
