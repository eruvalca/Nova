using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Features.Teams;
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
#pragma warning disable MA0051 // Keep this complete setup, operation, and assertion sequence together as one regression scenario.
    public async Task GetRosterReturnsFilteredRowsForApprovedClubMemberAsync()
#pragma warning restore MA0051
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using (var anonymousResponse = await anonymousClient.GetAsync(new Uri(TeamRosterEndpoints.GetRoster, UriKind.RelativeOrAbsolute), cancellationToken))
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
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            .Where(user => user.NormalizedEmail == email.ToUpperInvariant())
#pragma warning restore CA1862
            .Select(user => user.Id)
            .SingleAsync(cancellationToken);
        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Roster Season",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = userId
        };
        context.Seasons.Add(season);
        await context.SaveChangesAsync(cancellationToken);
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Roster Campaign",
            StartDate = new DateOnly(2026, 1, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = userId
        };
        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Roster",
            LastName = "Player",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            ClubId = club.ClubId,
            CreatedById = userId
        };
        var alpha = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Alpha",
            GraduationYear = 2030,
            ClubId = club.ClubId,
            CreatedById = userId
        };
        var beta = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Beta",
            GraduationYear = 2031,
            ClubId = club.ClubId,
            CreatedById = userId
        };
        var archived = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
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
new Uri(TeamRosterEndpoints.GetRosterUrl(search: "a", graduationYear: 2030), UriKind.RelativeOrAbsolute),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<TeamRosterItem>>(cancellationToken);
        rows.ShouldNotBeNull();
        rows.Select(row => row.Name).ShouldBe(["Alpha"]);
        rows[0].ActivePlacementCount.ShouldBe(1);
    }

    /// <summary>
    /// Verifies a non-administrator club member can read the team roster, covering the
    /// <c>RequireClubMember</c> policy the roster endpoint is authorized with.
    /// </summary>
    [Fact]
    public async Task GetRosterReturnsRowsForNonAdminClubMemberAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("team-roster-member-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var memberEmail = UniqueEmail("team-roster-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        long teamId;
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using (var context = fixture.CreateAdminContext())
#pragma warning restore MA0004
        {
            var adminUserId = await context.Users
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
                .Where(user => user.NormalizedEmail == adminEmail.ToUpperInvariant())
#pragma warning restore CA1862
                .Select(user => user.Id)
                .SingleAsync(cancellationToken);

            var team = new TeamEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Member Readable {Guid.CreateVersion7():N}",
                GraduationYear = 2030,
                ClubId = club.ClubId,
                CreatedById = adminUserId
            };
            context.Teams.Add(team);
            await context.SaveChangesAsync(cancellationToken);
            teamId = team.TeamId;
        }

        using var response = await memberClient.GetAsync(new Uri(TeamRosterEndpoints.GetRoster, UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<TeamRosterItem>>(cancellationToken);
        rows.ShouldNotBeNull();
        rows.Select(row => row.TeamId).ShouldContain(teamId);
    }

    /// <summary>
    /// Verifies LIKE metacharacters in the search term are matched literally by PostgreSQL rather
    /// than acting as wildcards. This is the authoritative check for the escaping fix, because the
    /// SQLite unit-test harness uses a literal <c>Contains</c> and cannot reproduce the bug.
    /// </summary>
    [Fact]
    public async Task GetRosterSearchTreatsLikeMetacharactersAsLiteralsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var email = UniqueEmail("team-roster-escaping");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using (var context = fixture.CreateAdminContext())
#pragma warning restore MA0004
        {
            var userId = await context.Users
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
                .Where(user => user.NormalizedEmail == email.ToUpperInvariant())
#pragma warning restore CA1862
                .Select(user => user.Id)
                .SingleAsync(cancellationToken);

            context.Teams.AddRange(
                NewTeam("50% Wins", club.ClubId, userId),
                NewTeam("50 Losses", club.ClubId, userId),
                NewTeam("a_b Squad", club.ClubId, userId),
                NewTeam("axb Squad", club.ClubId, userId),
                NewTeam(@"Path\Team", club.ClubId, userId),
                NewTeam("PathTeam", club.ClubId, userId));
            await context.SaveChangesAsync(cancellationToken);
        }

        using (var percentResponse = await client.GetAsync(
new Uri(TeamRosterEndpoints.GetRosterUrl(search: "50%"), UriKind.RelativeOrAbsolute),
            cancellationToken))
        {
            percentResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var rows = await percentResponse.Content.ReadFromJsonAsync<List<TeamRosterItem>>(cancellationToken);
            rows.ShouldNotBeNull();
            rows.Select(row => row.Name).ShouldBe(["50% Wins"]);
        }

        using var underscoreResponse = await client.GetAsync(
new Uri(TeamRosterEndpoints.GetRosterUrl(search: "a_b"), UriKind.RelativeOrAbsolute),
            cancellationToken);
        underscoreResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var underscoreRows = await underscoreResponse.Content.ReadFromJsonAsync<List<TeamRosterItem>>(cancellationToken);
        underscoreRows.ShouldNotBeNull();
        underscoreRows.Select(row => row.Name).ShouldBe(["a_b Squad"]);

        using var backslashResponse = await client.GetAsync(
new Uri(TeamRosterEndpoints.GetRosterUrl(search: @"Path\T"), UriKind.RelativeOrAbsolute),
            cancellationToken);
        backslashResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var backslashRows = await backslashResponse.Content.ReadFromJsonAsync<List<TeamRosterItem>>(cancellationToken);
        backslashRows.ShouldNotBeNull();
        backslashRows.Select(row => row.Name).ShouldBe([@"Path\Team"]);
    }

    /// <summary>
    /// Verifies a bounded explicit limit returns exactly the first teams in deterministic
    /// (Name, then TeamId) order.
    /// </summary>
    [Fact]
    public async Task GetRosterAppliesLimitReturnsFirstTeamsInDeterministicOrderAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var (club, email) = await SeedRosterClubAsync(client, cancellationToken);
        await SeedingHelpers.InsertTeamAsync(fixture, club.ClubId, email, "Alpha", 2030, cancellationToken);
        await SeedingHelpers.InsertTeamAsync(fixture, club.ClubId, email, "Bravo", 2030, cancellationToken);
        await SeedingHelpers.InsertTeamAsync(fixture, club.ClubId, email, "Charlie", 2030, cancellationToken);

        using var response = await client.GetAsync(new Uri("/api/teams?limit=2", UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<TeamRosterItem>>(cancellationToken);
        rows.ShouldNotBeNull();
        rows.Select(row => row.Name).ShouldBe(["Alpha", "Bravo"]);
    }

    /// <summary>
    /// Verifies that omitting the limit keeps the existing unbounded behavior at the endpoint boundary.
    /// </summary>
    [Fact]
    public async Task GetRosterOmittedLimitReturnsEveryMatchingTeamAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var (club, email) = await SeedRosterClubAsync(client, cancellationToken);
        await SeedingHelpers.InsertTeamAsync(fixture, club.ClubId, email, "Alpha", 2030, cancellationToken);
        await SeedingHelpers.InsertTeamAsync(fixture, club.ClubId, email, "Bravo", 2030, cancellationToken);
        await SeedingHelpers.InsertTeamAsync(fixture, club.ClubId, email, "Charlie", 2030, cancellationToken);

        using var response = await client.GetAsync(new Uri(TeamRosterEndpoints.GetRoster, UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rows = await response.Content.ReadFromJsonAsync<List<TeamRosterItem>>(cancellationToken);
        rows.ShouldNotBeNull();
        rows.Select(row => row.Name).ShouldBe(["Alpha", "Bravo", "Charlie"]);
    }

    /// <summary>
    /// Verifies explicit limit values outside the documented 1..200 cap are rejected with
    /// validation ProblemDetails before the handler runs.
    /// </summary>
    /// <param name="limit">The out-of-range explicit limit.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(201)]
    public async Task GetRosterInvalidLimitReturnsValidationProblemAsync(int limit)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await SeedRosterClubAsync(client, cancellationToken);

        using var response = await client.GetAsync(new Uri($"/api/teams?limit={limit}", UriKind.RelativeOrAbsolute), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("errors")
            .TryGetProperty(nameof(GetTeamRosterInput.Limit), out _)
            .ShouldBeTrue();
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Registers a club-owning user, creates the club, and refreshes the membership cookie.
    /// </summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club and the owning user's e-mail address.</returns>
    private async Task<(ClubDto Club, string Email)> SeedRosterClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var email = UniqueEmail("team-roster-limit");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        return (club, email);
    }

    /// <summary>
    /// Creates an unsaved active team entity for roster seeding.
    /// </summary>
    /// <param name="name">The team name.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="createdById">The creating user identifier.</param>
    /// <returns>A new team entity.</returns>
    private static TeamEntity NewTeam(string name, long clubId, long createdById) => new()
    {
        CreationOperationId = Guid.NewGuid(),
        Name = name,
        GraduationYear = 2030,
        ClubId = clubId,
        CreatedById = createdById
    };

    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
    {
        var context = fixture.CreateAdminContext();
        await using (context)
        {
            var user = await context.Users.SingleAsync(
#pragma warning disable CA1862 // Compare normalized values in SQL; EF does not translate StringComparison overloads.
            candidate => candidate.NormalizedEmail == email.ToUpperInvariant(),
#pragma warning restore CA1862
            cancellationToken);
            user.ClubId = clubId;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var responseRequestContent = SeedingHelpers.CreateClubMultipartContent($"Team Roster Club {Guid.CreateVersion7():N}", "Austin", "TX");
        using var response = await client.PostAsync(
        new Uri(ClubEndpoints.Create, UriKind.RelativeOrAbsolute),
                    responseRequestContent,
                    cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"{ClubEndpoints.Complete}?returnUrl=/dashboard", UriKind.RelativeOrAbsolute), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";
}
