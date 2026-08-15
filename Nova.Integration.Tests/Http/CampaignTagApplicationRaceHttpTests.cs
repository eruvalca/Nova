using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Cross-slice HTTP coverage for the duplicate tag-application race: when two approved club
/// members apply the same tag to the same assignment concurrently, exactly one request
/// succeeds, the other receives a clear conflict, and exactly one durable row exists.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignTagApplicationRaceHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies that two concurrent tag applications for the same (assignment, tag) pair yield
    /// one Created response, one Conflict response, and a single durable database row.
    /// </summary>
    [Fact]
    public async Task ParallelTagApplication_ForSameAssignmentAndTag_YieldsOneCreatedOneConflict_WithSingleDurableRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (firstClient, secondClient, adminEmail, assignmentId) = await SeedTwoMemberClubWithTagAsync(
            "tag-race", cancellationToken);
        var tagId = await InsertTagDefinitionAsync(assignmentId, adminEmail, "Winger", cancellationToken);

        var applyInput = () => new ApplyCampaignTagApplicationInput
        {
            PlayerCampaignAssignmentId = assignmentId,
            PlayerTagId = tagId
        };

        // Start both requests before awaiting either so they race through the server
        // simultaneously; either request may win, so both orderings are tolerated.
        var applyA = firstClient.PostAsJsonAsync(CampaignEndpoints.ApplyCampaignTagApplication, applyInput(), cancellationToken);
        var applyB = secondClient.PostAsJsonAsync(CampaignEndpoints.ApplyCampaignTagApplication, applyInput(), cancellationToken);
        using var responseA = await applyA;
        using var responseB = await applyB;

        var statuses = new[] { responseA.StatusCode, responseB.StatusCode };
        statuses.Count(status => status == HttpStatusCode.Created).ShouldBe(1);
        statuses.Count(status => status == HttpStatusCode.Conflict).ShouldBe(1);

        await using var context = fixture.CreateAdminContext();
        var durableRows = await context.CampaignTagApplications
            .Where(candidate => candidate.PlayerCampaignAssignmentId == assignmentId
                && candidate.PlayerTagId == tagId)
            .ToListAsync(cancellationToken);
        durableRows.Count.ShouldBe(1);
    }

    /// <summary>
    /// Seeds two approved members in one club and an active campaign with one participant.
    /// </summary>
    /// <param name="prefix">A stable e-mail prefix.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The two member clients, the admin e-mail, and the assignment identifier.</returns>
    private async Task<(HttpClient FirstClient, HttpClient SecondClient, string AdminEmail, long AssignmentId)>
        SeedTwoMemberClubWithTagAsync(string prefix, CancellationToken cancellationToken)
    {
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail($"{prefix}-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var firstClient = fixture.CreateNovaHttpClient();
        var firstEmail = UniqueEmail($"{prefix}-first");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(firstClient, firstEmail, Password, cancellationToken);
        await UpdateUserAsync(firstEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(firstClient, cancellationToken);

        var secondClient = fixture.CreateNovaHttpClient();
        var secondEmail = UniqueEmail($"{prefix}-second");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(secondClient, secondEmail, Password, cancellationToken);
        await UpdateUserAsync(secondEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(secondClient, cancellationToken);

        var assignmentId = await SeedCampaignDataAsync(club.ClubId, adminEmail, prefix, cancellationToken);
        return (firstClient, secondClient, adminEmail, assignmentId);
    }

    /// <summary>
    /// Seeds an active season, campaign, player, and participation for the given club.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="email">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="prefix">A stable name prefix.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The participation identifier.</returns>
    private async Task<long> SeedCampaignDataAsync(
        long clubId,
        string email,
        string prefix,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var season = new SeasonEntity { Name = $"{prefix} Season {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = user.Id };
        var campaign = new CampaignEntity
        {
            Name = $"{prefix} Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            Season = season,
            SeasonId = 0,
            ClubId = clubId,
            CreatedById = user.Id
        };
        var player = new PlayerEntity
        {
            FirstName = prefix,
            LastName = $"Player {suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = user.Id
        };

        context.AddRange(season, campaign, player);
        await context.SaveChangesAsync(cancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = clubId,
            CreatedById = user.Id,
            PlacementOutcome = PlacementOutcome.Undecided,
            TryoutNumber = 7
        };
        context.Add(assignment);
        await context.SaveChangesAsync(cancellationToken);

        return assignment.PlayerCampaignAssignmentId;
    }

    /// <summary>
    /// Inserts an active tag definition for the club that owns the given assignment.
    /// </summary>
    /// <param name="assignmentId">The participation identifier whose club owns the new tag.</param>
    /// <param name="email">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="name">The tag name.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The new tag definition identifier.</returns>
    private async Task<long> InsertTagDefinitionAsync(long assignmentId, string email, string name, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var assignment = await context.PlayerCampaignAssignments
            .SingleAsync(candidate => candidate.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        var user = await context.Users
            .SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        var playerTag = new PlayerTagEntity
        {
            Name = name,
            Color = "#00CC00",
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = assignment.ClubId,
            CreatedById = user.Id
        };
        context.Add(playerTag);
        await context.SaveChangesAsync(cancellationToken);
        return playerTag.PlayerTagId;
    }

    /// <summary>
    /// Generates a unique e-mail address for a test user.
    /// </summary>
    /// <param name="prefix">A stable prefix included in the address.</param>
    /// <returns>A unique e-mail address.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    /// <summary>
    /// Creates a club and returns the resulting club DTO.
    /// </summary>
    /// <param name="client">The HTTP client used to create the club.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club.</returns>
    private static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            ClubEndpoints.Create,
            new CreateClubInput { Name = $"Club {Guid.NewGuid():N}", City = "X", State = "TX" },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    /// <summary>
    /// Completes the club-membership flow so the client carries the refreshed membership cookie.
    /// </summary>
    /// <param name="client">The HTTP client whose membership cookie should be refreshed.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the cookie has been refreshed.</returns>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>
    /// Updates a user's club membership directly in the database.
    /// </summary>
    /// <param name="email">The user's e-mail address.</param>
    /// <param name="clubId">The club identifier to assign, or <see langword="null"/> to clear membership.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the user has been updated.</returns>
    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        user.ClubId = clubId;
        await context.SaveChangesAsync(cancellationToken);
    }
}
