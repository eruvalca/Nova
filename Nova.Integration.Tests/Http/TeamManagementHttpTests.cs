using System.Net;
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

        using var response = await client.PostAsJsonAsync(ClubEndpoints.Create, new CreateClubInput
        {
            Name = $"{clubName} {Guid.CreateVersion7():N}",
            City = "Austin",
            State = "TX"
        }, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();

        using var refresh = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
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
