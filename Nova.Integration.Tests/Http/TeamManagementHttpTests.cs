using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Teams;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the team creation endpoint, focused on the response contract that
/// route-metadata assertions cannot prove: the 201 status and the generated <c>Location</c> header.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TeamManagementHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies creating a team returns 201 with a <c>Location</c> header pointing at the team-detail
    /// route, and that following the header resolves the created team.
    /// </summary>
    [Fact]
    public async Task CreateTeam_ReturnsCreatedWithLocationHeader_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "team-create-location-admin", "Location Lancers", cancellationToken);

        var teamName = $"Team-{Guid.CreateVersion7():N}";
        using var response = await client.PostAsJsonAsync(
            TeamEndpoints.Create,
            new CreateTeamInput { Name = teamName, GraduationYear = 2031 },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<TeamDto>(cancellationToken);
        created.ShouldNotBeNull();
        created.Name.ShouldBe(teamName);

        response.Headers.Location.ShouldNotBeNull();
        var location = response.Headers.Location.IsAbsoluteUri
            ? response.Headers.Location.AbsolutePath
            : response.Headers.Location.ToString();
        location.ShouldBe(TeamEndpoints.GetDetailUrl(created.TeamId));

        using var followed = await client.GetAsync(location, cancellationToken);
        followed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies the create endpoint rejects anonymous callers.
    /// </summary>
    [Fact]
    public async Task CreateTeam_ReturnsUnauthorized_ForAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.PostAsJsonAsync(
            TeamEndpoints.Create,
            new CreateTeamInput { Name = "Anonymous Team", GraduationYear = 2031 },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies a second create with the same name and graduation year is rejected as a conflict by
    /// the unique <c>(ClubId, Name, GraduationYear)</c> index.
    /// </summary>
    [Fact]
    public async Task CreateTeam_ReturnsConflict_ForDuplicateNameAndGraduationYear()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(client, "team-create-duplicate-admin", "Duplicate Dynamos", cancellationToken);

        var teamName = $"Team-{Guid.CreateVersion7():N}";
        var input = new CreateTeamInput { Name = teamName, GraduationYear = 2031 };

        using (var first = await client.PostAsJsonAsync(TeamEndpoints.Create, input, cancellationToken))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using (var second = await client.PostAsJsonAsync(TeamEndpoints.Create, input, cancellationToken))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        await using var verify = fixture.CreateAdminContext();
        var count = await verify.Teams
            .CountAsync(team => team.ClubId == club.ClubId && team.Name == teamName, cancellationToken);
        count.ShouldBe(1);
    }

    /// <summary>
    /// Verifies a non-admin club member cannot create a team.
    /// </summary>
    [Fact]
    public async Task CreateTeam_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(adminClient, "team-create-member-admin", "Member Blockers", cancellationToken);
        await RegisterClubMemberAsync(memberClient, "team-create-member", club.ClubId, cancellationToken);

        using var response = await memberClient.PostAsJsonAsync(
            TeamEndpoints.Create,
            new CreateTeamInput { Name = $"Team-{Guid.CreateVersion7():N}", GraduationYear = 2031 },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies the team update endpoint rejects anonymous callers.
    /// </summary>
    [Fact]
    public async Task UpdateTeam_ReturnsUnauthorized_ForAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.PutAsJsonAsync(
            TeamEndpoints.UpdateUrl(1),
            new UpdateTeamInput { TeamId = 1, Name = "Anonymous", GraduationYear = 2031 },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies a non-admin club member cannot update a team in their own club.
    /// </summary>
    [Fact]
    public async Task UpdateTeam_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var club = await RegisterClubAdminAsync(adminClient, "team-update-member-admin", "Update Blockers", cancellationToken);
        var team = await CreateTeamAsync(adminClient, cancellationToken);
        await RegisterClubMemberAsync(memberClient, "team-update-member", club.ClubId, cancellationToken);

        using var response = await memberClient.PutAsJsonAsync(
            TeamEndpoints.UpdateUrl(team.TeamId),
            new UpdateTeamInput { TeamId = team.TeamId, Name = "Hijacked", GraduationYear = team.GraduationYear },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a club administrator can update a team and receives the updated DTO.
    /// </summary>
    [Fact]
    public async Task UpdateTeam_ReturnsOk_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        await RegisterClubAdminAsync(client, "team-update-admin", "Update Lancers", cancellationToken);
        var team = await CreateTeamAsync(client, cancellationToken);
        var updatedName = $"Updated-{Guid.CreateVersion7():N}";

        using var response = await client.PutAsJsonAsync(
            TeamEndpoints.UpdateUrl(team.TeamId),
            new UpdateTeamInput { TeamId = team.TeamId, Name = updatedName, GraduationYear = team.GraduationYear },
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<TeamDto>(cancellationToken);
        updated.ShouldNotBeNull();
        updated.Name.ShouldBe(updatedName);
    }

    /// <summary>
    /// Creates a team through the HTTP API and returns its DTO.
    /// </summary>
    private async Task<TeamDto> CreateTeamAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            TeamEndpoints.Create,
            new CreateTeamInput { Name = $"Team-{Guid.CreateVersion7():N}", GraduationYear = 2031 },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var team = await response.Content.ReadFromJsonAsync<TeamDto>(cancellationToken);
        team.ShouldNotBeNull();
        return team;
    }

    /// <summary>
    /// Registers a completed-profile user as a member of an existing club and refreshes their claims.
    /// </summary>
    private async Task RegisterClubMemberAsync(
        HttpClient client,
        string emailPrefix,
        long clubId,
        CancellationToken cancellationToken)
    {
        var email = SeedingHelpers.UniqueEmail(emailPrefix);
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, clubId, cancellationToken, "Team", "Member");
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
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
        var email = $"{emailPrefix}-{Guid.CreateVersion7():N}@example.com";
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, "Club", "Admin", cancellationToken);

        using var response = await client.PostAsync(
            ClubEndpoints.Create,
            SeedingHelpers.CreateClubMultipartContent($"{clubName} {Guid.CreateVersion7():N}", "Austin", "TX"),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();

        using var refresh = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/dashboard", cancellationToken);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Found);

        return club;
    }

    /// <summary>
    /// Updates seeded Identity user names using the admin context.
    /// </summary>
    /// <param name="email">The user email to update.</param>
    /// <param name="firstName">The first name.</param>
    /// <param name="lastName">The last name.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when persisted.</returns>
    private async Task UpdateUserAsync(
        string email,
        string firstName,
        string lastName,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var normalizedEmail = email.ToUpperInvariant();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.ClubId = null;
        context.Users.Update(user);
        await context.SaveChangesAsync(cancellationToken);
    }
}
