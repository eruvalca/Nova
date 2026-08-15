using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.Shared.Enums;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// The seeded evaluation workspace a browser scenario runs against.
/// </summary>
/// <param name="ClubId">The owning club identifier.</param>
/// <param name="CampaignId">The active campaign identifier.</param>
/// <param name="CampaignName">The active campaign name.</param>
/// <param name="AdminUserId">The club administrator's user identifier.</param>
/// <param name="AdminEmail">The club administrator's login e-mail.</param>
/// <param name="EvaluatorUserId">The approved evaluator's user identifier.</param>
/// <param name="EvaluatorEmail">The approved evaluator's login e-mail.</param>
/// <param name="AssignmentIds">All participant assignment identifiers in seeded order.</param>
/// <param name="ArchivedTagApplicationAssignmentId">The assignment carrying a pre-applied archived tag.</param>
/// <param name="ActiveTagName">An active tag-definition name available for application.</param>
/// <param name="SecondActiveTagName">A second active tag-definition name.</param>
/// <param name="ArchivedTagName">The archived tag-definition name (pre-applied on one participant).</param>
public sealed record SeededEvaluationWorkspace(
    long ClubId,
    long CampaignId,
    string CampaignName,
    long AdminUserId,
    string AdminEmail,
    long EvaluatorUserId,
    string EvaluatorEmail,
    IReadOnlyList<long> AssignmentIds,
    long ArchivedTagApplicationAssignmentId,
    string ActiveTagName,
    string SecondActiveTagName,
    string ArchivedTagName);

/// <summary>
/// Seeds a complete evaluation workspace for browser scenarios: an administrator and an
/// approved evaluator (registered through the real Identity HTTP flow), a club, an active
/// campaign with 60 participants (two roster pages at the default page size of 50), two active
/// tag definitions, and one archived tag definition pre-applied to a participant.
/// </summary>
public static class EvaluationSeed
{
    /// <summary>The password shared by every seeded user.</summary>
    public const string Password = "Test#Passw0rd!";

    /// <summary>The number of participants seeded (two pages at the default page size of 50).</summary>
    public const int ParticipantCount = 60;

    /// <summary>
    /// Seeds the workspace and returns its identifiers plus the seeded user credentials.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded workspace.</returns>
    public static async Task<SeededEvaluationWorkspace> SeedAsync(
        NovaAppHostFixture fixture,
        CancellationToken cancellationToken)
    {
        // Register the club administrator and create the club (the create flow makes them the admin).
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("browser-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        // Register the approved evaluator.
        using var evaluatorClient = fixture.CreateNovaHttpClient();
        var evaluatorEmail = UniqueEmail("browser-evaluator");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(evaluatorClient, evaluatorEmail, Password, cancellationToken);

        long adminUserId;
        long evaluatorUserId;
        await using (var context = fixture.CreateAdminContext())
        {
            var admin = await context.Users.SingleAsync(user => user.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken);
            admin.FirstName = "Alice";
            admin.LastName = "Author";
            var evaluator = await context.Users.SingleAsync(user => user.NormalizedEmail == evaluatorEmail.ToUpperInvariant(), cancellationToken);
            evaluator.FirstName = "Bob";
            evaluator.LastName = "Observer";
            evaluator.ClubId = club.ClubId;
            await context.SaveChangesAsync(cancellationToken);
            adminUserId = admin.Id;
            evaluatorUserId = evaluator.Id;
        }

        var (campaignId, campaignName, assignmentIds, archivedApplicationAssignmentId, activeTagName, secondActiveTagName, archivedTagName) =
            await SeedWorkspaceDataAsync(fixture, club.ClubId, adminUserId, cancellationToken);

        return new SeededEvaluationWorkspace(
            club.ClubId,
            campaignId,
            campaignName,
            adminUserId,
            adminEmail,
            evaluatorUserId,
            evaluatorEmail,
            assignmentIds,
            archivedApplicationAssignmentId,
            activeTagName,
            secondActiveTagName,
            archivedTagName);
    }

    /// <summary>
    /// Seeds the season, campaign, participants, and tag definitions directly through the admin
    /// context. Every participant has a final outcome (Not selected) so the campaign can be
    /// closed by the stale-close scenario.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminUserId">The administrator's user identifier used for created-by stamping.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The campaign identifier, participant identifiers, archived-application assignment, and tag names.</returns>
    private static async Task<(
        long CampaignId,
        string CampaignName,
        IReadOnlyList<long> AssignmentIds,
        long ArchivedTagApplicationAssignmentId,
        string ActiveTagName,
        string SecondActiveTagName,
        string ArchivedTagName)> SeedWorkspaceDataAsync(
        NovaAppHostFixture fixture,
        long clubId,
        long adminUserId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");

        var season = new SeasonEntity
        {
            Name = $"Browser Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubId,
            CreatedById = adminUserId
        };
        var campaign = new CampaignEntity
        {
            Name = $"Browser Tryouts {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            Season = season,
            SeasonId = 0,
            ClubId = clubId,
            CreatedById = adminUserId
        };
        context.AddRange(season, campaign);
        await context.SaveChangesAsync(cancellationToken);

        var activeTagName = $"Winger {suffix}";
        var secondActiveTagName = $"Keeper {suffix}";
        var archivedTagName = $"Legacy {suffix}";
        var activeTag = new PlayerTagEntity
        {
            Name = activeTagName,
            Color = "#00CC00",
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = adminUserId
        };
        var secondActiveTag = new PlayerTagEntity
        {
            Name = secondActiveTagName,
            Color = "#CC0000",
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = adminUserId
        };
        var archivedTag = new PlayerTagEntity
        {
            Name = archivedTagName,
            Color = "#999999",
            LifecycleStatus = LifecycleStatus.Archived,
            ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ArchivedById = adminUserId,
            ClubId = clubId,
            CreatedById = adminUserId
        };
        context.AddRange(activeTag, secondActiveTag, archivedTag);
        await context.SaveChangesAsync(cancellationToken);

        var players = new List<PlayerEntity>(ParticipantCount);
        for (var index = 1; index <= ParticipantCount; index++)
        {
            players.Add(new PlayerEntity
            {
                FirstName = "Browser",
                LastName = $"Player {index:D2}",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030 + (index % 3),
                LifecycleStatus = LifecycleStatus.Active,
                ClubId = clubId,
                CreatedById = adminUserId
            });
        }

        context.AddRange(players);
        await context.SaveChangesAsync(cancellationToken);

        var assignments = players.Select(player => new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = clubId,
            CreatedById = adminUserId,
            PlacementOutcome = PlacementOutcome.NotSelected,
            TryoutNumber = players.IndexOf(player) + 1
        }).ToList();
        context.AddRange(assignments);
        await context.SaveChangesAsync(cancellationToken);

        // Capture the generated identifiers in seeded order.
        var assignmentIds = await context.PlayerCampaignAssignments
            .Where(candidate => candidate.CampaignId == campaign.CampaignId)
            .OrderBy(candidate => candidate.TryoutNumber)
            .Select(candidate => candidate.PlayerCampaignAssignmentId)
            .ToListAsync(cancellationToken);

        // Pre-apply the archived tag to the first participant so the archived-definition
        // scenario starts from an existing application.
        var archivedApplication = new CampaignTagApplicationEntity
        {
            PlayerCampaignAssignmentId = assignmentIds[0],
            PlayerTagId = archivedTag.PlayerTagId,
            ClubId = clubId,
            CreatedById = adminUserId
        };
        context.Add(archivedApplication);
        await context.SaveChangesAsync(cancellationToken);

        return (
            campaign.CampaignId,
            campaign.Name,
            assignmentIds,
            assignmentIds[0],
            activeTagName,
            secondActiveTagName,
            archivedTagName);
    }

    /// <summary>
    /// Generates a unique e-mail address for a seeded user.
    /// </summary>
    /// <param name="prefix">A stable prefix included in the address.</param>
    /// <returns>A unique e-mail address.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    /// <summary>
    /// Creates a club through the real HTTP endpoint and returns the club DTO.
    /// </summary>
    /// <param name="client">The authenticated HTTP client.</param>
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
}
