using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.Shared.Enums;
using Nova.Shared.Features.Account;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// The seeded closeout workspace a browser scenario runs against.
/// </summary>
/// <param name="ClubId">The owning club identifier.</param>
/// <param name="AdminUserId">The club administrator's user identifier.</param>
/// <param name="AdminEmail">The club administrator's login e-mail.</param>
/// <param name="SecondAdminUserId">The second club administrator's user identifier.</param>
/// <param name="SecondAdminEmail">The second club administrator's login e-mail.</param>
/// <param name="EvaluatorUserId">The approved evaluator's user identifier.</param>
/// <param name="EvaluatorEmail">The approved evaluator's login e-mail.</param>
/// <param name="BlockedCampaignId">The blocked active campaign identifier.</param>
/// <param name="BlockedAssignmentIds">The blocked campaign's participant assignment identifiers in tryout-number order.</param>
/// <param name="ReadyCampaignId">The ready active campaign identifier.</param>
/// <param name="ClosedCampaignId">The closed campaign identifier.</param>
/// <param name="EligibleTeamId">An active team eligible for every seeded player.</param>
/// <param name="EligibleTeamName">The eligible team's display name.</param>
public sealed record SeededCloseoutWorkspace(
    long ClubId,
    long AdminUserId,
    string AdminEmail,
    long SecondAdminUserId,
    string SecondAdminEmail,
    long EvaluatorUserId,
    string EvaluatorEmail,
    long BlockedCampaignId,
    IReadOnlyList<long> BlockedAssignmentIds,
    long ReadyCampaignId,
    long ClosedCampaignId,
    long EligibleTeamId,
    string EligibleTeamName);

/// <summary>
/// Seeds the closeout workspace for browser scenarios: an administrator, a second administrator
/// (promoted through the real assign-ClubAdmin endpoint), an approved evaluator, a club, an eligible
/// team, a blocked campaign (one undecided, one ineligible, one archived-team assignment), a ready
/// campaign, and a closed campaign carrying a real <c>Closed</c> lifecycle event.
/// </summary>
public static class CloseoutSeed
{
    /// <summary>The password shared by every seeded user.</summary>
    public const string Password = "Test#Passw0rd!";

    /// <summary>The graduation year assigned to every seeded player.</summary>
    private const int PlayerGraduationYear = 2030;

    /// <summary>
    /// Seeds the closeout workspace and returns its identifiers plus the seeded user credentials.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded closeout workspace.</returns>
    public static async Task<SeededCloseoutWorkspace> SeedAsync(
        NovaAppHostFixture fixture,
        CancellationToken cancellationToken)
    {
        // Register the club administrator and create the club (the create flow makes them the admin).
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("closeout-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken, firstName: "Alice", lastName: "Author");
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        // Register the approved evaluator.
        using var evaluatorClient = fixture.CreateNovaHttpClient();
        var evaluatorEmail = SeedingHelpers.UniqueEmail("closeout-evaluator");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(evaluatorClient, evaluatorEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, evaluatorEmail, club.ClubId, cancellationToken, firstName: "Bob", lastName: "Observer");

        // Register a second administrator and promote them through the real assign-ClubAdmin endpoint
        // so the stale-blocked-close scenario uses independent, authoritative administrator cookies.
        using var secondAdminClient = fixture.CreateNovaHttpClient();
        var secondAdminEmail = SeedingHelpers.UniqueEmail("closeout-second-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(secondAdminClient, secondAdminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, secondAdminEmail, club.ClubId, cancellationToken, firstName: "Carol", lastName: "Reviewer");
        await SeedingHelpers.RefreshClubMembershipCookieAsync(secondAdminClient, cancellationToken);

        long adminUserId;
        long secondAdminUserId;
        long evaluatorUserId;
        await using (var context = fixture.CreateAdminContext())
        {
            adminUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken)).Id;
            secondAdminUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == secondAdminEmail.ToUpperInvariant(), cancellationToken)).Id;
            evaluatorUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == evaluatorEmail.ToUpperInvariant(), cancellationToken)).Id;
        }

        using (var promotion = await adminClient.PostAsync(
                   ClubEndpoints.PromoteMemberUrl(secondAdminUserId), null, cancellationToken))
        {
            promotion.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await SeedingHelpers.RefreshClubMembershipCookieAsync(secondAdminClient, cancellationToken);

        var suffix = Guid.NewGuid().ToString("N");
        var eligibleTeamName = $"Alpha {suffix}";
        var eligibleTeamId = await SeedingHelpers.InsertTeamAsync(
            fixture, club.ClubId, adminEmail, eligibleTeamName, graduationYear: 2029, cancellationToken);

        var (blockedCampaignId, blockedAssignmentIds) = await SeedBlockedCampaignAsync(
            fixture, club.ClubId, adminUserId, suffix, cancellationToken);

        var ready = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture, club.ClubId, adminEmail, "Closeout Ready", participantCount: 3, PlacementOutcome.NotSelected, cancellationToken);

        var closed = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture, club.ClubId, adminEmail, "Closeout Closed", participantCount: 3, PlacementOutcome.NotSelected, cancellationToken);
        await SeedingHelpers.CloseCampaignThroughServiceAsync(
            fixture, club.ClubId, adminUserId, closed.CampaignId, cancellationToken);

        return new SeededCloseoutWorkspace(
            club.ClubId,
            adminUserId,
            adminEmail,
            secondAdminUserId,
            secondAdminEmail,
            evaluatorUserId,
            evaluatorEmail,
            blockedCampaignId,
            blockedAssignmentIds,
            ready.CampaignId,
            closed.CampaignId,
            eligibleTeamId,
            eligibleTeamName);
    }

    /// <summary>
    /// Seeds the blocked campaign with three participants: one undecided (outcomes blocker), one
    /// assigned to an ineligible team (eligibility blocker), and one assigned to an archived team
    /// (archived-teams blocker). Counts are deterministically 1/1/1.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminUserId">The acting administrator's user identifier.</param>
    /// <param name="suffix">A stable name suffix.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The blocked campaign identifier and its participant assignment identifiers in tryout-number order.</returns>
    private static async Task<(long CampaignId, IReadOnlyList<long> AssignmentIds)> SeedBlockedCampaignAsync(
        NovaAppHostFixture fixture,
        long clubId,
        long adminUserId,
        string suffix,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Closeout Blocked Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubId,
            CreatedById = adminUserId
        };
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Closeout Blocked Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            Season = season,
            SeasonId = 0,
            ClubId = clubId,
            CreatedById = adminUserId
        };
        context.AddRange(season, campaign);
        await context.SaveChangesAsync(cancellationToken);
        var club = await context.Clubs.SingleAsync(
            candidate => candidate.ClubId == clubId,
            cancellationToken);
        club.CurrentSeasonId = season.SeasonId;
        await context.SaveChangesAsync(cancellationToken);

        var players = new List<PlayerEntity>(3);
        for (var index = 1; index <= 3; index++)
        {
            players.Add(new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                FirstName = "Blocked",
                LastName = $"Player {index:D2} {suffix}",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = PlayerGraduationYear,
                LifecycleStatus = LifecycleStatus.Active,
                ClubId = clubId,
                CreatedById = adminUserId
            });
        }

        var ineligibleTeam = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Ineligible {suffix}",
            GraduationYear = PlayerGraduationYear + 1,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = adminUserId
        };
        var archivedTeam = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Archived {suffix}",
            GraduationYear = PlayerGraduationYear - 1,
            LifecycleStatus = LifecycleStatus.Archived,
            ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ArchivedById = adminUserId,
            ClubId = clubId,
            CreatedById = adminUserId
        };

        context.AddRange(players);
        context.AddRange(ineligibleTeam, archivedTeam);
        await context.SaveChangesAsync(cancellationToken);

        var assignments = new List<PlayerCampaignAssignmentEntity>
        {
            new()
            {
                PlayerId = players[0].PlayerId,
                CampaignId = campaign.CampaignId,
                ClubId = clubId,
                CreatedById = adminUserId,
                PlacementOutcome = PlacementOutcome.Undecided,
                TryoutNumber = 1
            },
            new()
            {
                PlayerId = players[1].PlayerId,
                CampaignId = campaign.CampaignId,
                ClubId = clubId,
                CreatedById = adminUserId,
                PlacementOutcome = PlacementOutcome.Assigned,
                TeamId = ineligibleTeam.TeamId,
                TryoutNumber = 2
            },
            new()
            {
                PlayerId = players[2].PlayerId,
                CampaignId = campaign.CampaignId,
                ClubId = clubId,
                CreatedById = adminUserId,
                PlacementOutcome = PlacementOutcome.Assigned,
                TeamId = archivedTeam.TeamId,
                TryoutNumber = 3
            }
        };
        context.AddRange(assignments);
        await context.SaveChangesAsync(cancellationToken);

        var assignmentIds = await context.PlayerCampaignAssignments
            .Where(assignment => assignment.CampaignId == campaign.CampaignId)
            .OrderBy(assignment => assignment.TryoutNumber)
            .Select(assignment => assignment.PlayerCampaignAssignmentId)
            .ToListAsync(cancellationToken);

        return (campaign.CampaignId, assignmentIds);
    }
}
