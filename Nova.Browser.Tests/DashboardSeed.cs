using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.Shared.Enums;

namespace Nova.Browser.Tests;

/// <summary>
/// The seeded dashboard workspace a browser scenario runs against.
/// </summary>
/// <param name="ClubId">The owning club identifier.</param>
/// <param name="AdminUserId">The club administrator's user identifier.</param>
/// <param name="AdminEmail">The club administrator's login e-mail.</param>
/// <param name="EvaluatorUserId">The approved evaluator's user identifier.</param>
/// <param name="EvaluatorEmail">The approved evaluator's login e-mail.</param>
/// <param name="ApplicantUserId">The pending-applicant user identifier.</param>
/// <param name="ApplicantEmail">The pending-applicant login e-mail.</param>
/// <param name="PhotoLessEmail">The photo-less user's login e-mail (registration only).</param>
/// <param name="ClubLessEmail">The photo-complete club-less user's login e-mail.</param>
/// <param name="UndecidedCampaignId">The active campaign carrying undecided participants.</param>
/// <param name="UndecidedCampaignName">The active undecided campaign's display name.</param>
/// <param name="DecidedCampaignId">The second active campaign with a decided participant.</param>
/// <param name="DecidedCampaignName">The second active campaign's display name.</param>
/// <param name="FirstUnresolvedCampaignId">The campaign targeted by the administrator review-placements link.</param>
/// <param name="ActivePlayerCount">The seeded active player count.</param>
/// <param name="ArchivedPlayerCount">The seeded archived player count.</param>
/// <param name="ActiveTeamCount">The seeded active team count.</param>
/// <param name="ArchivedTeamCount">The seeded archived team count.</param>
/// <param name="PendingJoinRequestCount">The seeded pending join-request count.</param>
/// <param name="UnresolvedPlacementCount">The seeded whole-club unresolved placement count.</param>
public sealed record SeededDashboardWorkspace(
    long ClubId,
    long AdminUserId,
    string AdminEmail,
    long EvaluatorUserId,
    string EvaluatorEmail,
    long ApplicantUserId,
    string ApplicantEmail,
    string PhotoLessEmail,
    string ClubLessEmail,
    long UndecidedCampaignId,
    string UndecidedCampaignName,
    long DecidedCampaignId,
    string DecidedCampaignName,
    long FirstUnresolvedCampaignId,
    int ActivePlayerCount,
    int ArchivedPlayerCount,
    int ActiveTeamCount,
    int ArchivedTeamCount,
    int PendingJoinRequestCount,
    int UnresolvedPlacementCount);

/// <summary>
/// The seeded no-campaign dashboard workspace used by the empty-state browser scenario.
/// </summary>
/// <param name="ClubId">The owning club identifier.</param>
/// <param name="AdminEmail">The club administrator's login e-mail.</param>
/// <param name="EvaluatorEmail">The approved evaluator's login e-mail.</param>
public sealed record SeededEmptyDashboardWorkspace(
    long ClubId,
    string AdminEmail,
    string EvaluatorEmail);

/// <summary>
/// Seeds the club dashboard workspace for browser scenarios: an administrator, an approved evaluator,
/// a pending applicant, a photo-less user, a photo-complete club-less user, two active campaigns (one
/// with undecided participants), active + archived players and teams, one pending join request, and one
/// durable activity event so the evaluator sees a real recent-activity row with an actor snapshot.
/// </summary>
public static class DashboardSeed
{
    /// <summary>The password shared by every seeded user.</summary>
    public const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Seeds the populated dashboard workspace and returns its identifiers plus the seeded user credentials.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded dashboard workspace.</returns>
    public static async Task<SeededDashboardWorkspace> SeedAsync(
        NovaAppHostFixture fixture,
        CancellationToken cancellationToken)
    {
        // Register the club administrator and create the club (the create flow makes them the admin).
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("dashboard-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken, firstName: "Alice", lastName: "Author");
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        // Register the approved evaluator.
        using var evaluatorClient = fixture.CreateNovaHttpClient();
        var evaluatorEmail = SeedingHelpers.UniqueEmail("dashboard-evaluator");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(evaluatorClient, evaluatorEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, evaluatorEmail, club.ClubId, cancellationToken, firstName: "Bob", lastName: "Observer");
        await SeedingHelpers.RefreshClubMembershipCookieAsync(evaluatorClient, cancellationToken);

        // Register the pending applicant (photo-complete, no club).
        using var applicantClient = fixture.CreateNovaHttpClient();
        var applicantEmail = SeedingHelpers.UniqueEmail("dashboard-applicant");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(applicantClient, applicantEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, applicantEmail, clubId: null, cancellationToken, firstName: "Carol", lastName: "Candidate");

        // Register a photo-less user (registration only, no photo upload) and a photo-complete club-less user.
        using var photoLessClient = fixture.CreateNovaHttpClient();
        var photoLessEmail = SeedingHelpers.UniqueEmail("dashboard-photo-less");
        await IdentityHttpClientHelper.RegisterUserAsync(photoLessClient, photoLessEmail, Password, cancellationToken);

        using var clubLessClient = fixture.CreateNovaHttpClient();
        var clubLessEmail = SeedingHelpers.UniqueEmail("dashboard-club-less");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(clubLessClient, clubLessEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, clubLessEmail, clubId: null, cancellationToken, firstName: "Dana", lastName: "Drifter");

        long adminUserId;
        long evaluatorUserId;
        long applicantUserId;
        await using (var context = fixture.CreateAdminContext())
        {
            adminUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken)).Id;
            evaluatorUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == evaluatorEmail.ToUpperInvariant(), cancellationToken)).Id;
            applicantUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == applicantEmail.ToUpperInvariant(), cancellationToken)).Id;
        }

        var undecided = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture, club.ClubId, adminEmail, "Dash Undecided", participantCount: 2, PlacementOutcome.Undecided, cancellationToken);
        var decided = await SeedDecidedCampaignAsync(fixture, club.ClubId, adminEmail, cancellationToken);

        var suffix = Guid.NewGuid().ToString("N");
        await using (var seedContext = fixture.CreateAdminContext())
        {
            seedContext.AddRange(
                new PlayerEntity
                {
                    FirstName = "Archived",
                    LastName = "Player",
                    DateOfBirth = new DateOnly(2008, 5, 5),
                    GraduationYear = 2026,
                    LifecycleStatus = LifecycleStatus.Archived,
                    ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    ArchivedById = adminUserId,
                    ClubId = club.ClubId,
                    CreatedById = adminUserId
                },
                new TeamEntity { Name = $"Dash Active Team A {suffix}", GraduationYear = 2029, ClubId = club.ClubId, CreatedById = adminUserId },
                new TeamEntity { Name = $"Dash Active Team B {suffix}", GraduationYear = 2030, ClubId = club.ClubId, CreatedById = adminUserId },
                new TeamEntity
                {
                    Name = $"Dash Archived Team {suffix}",
                    GraduationYear = 2028,
                    LifecycleStatus = LifecycleStatus.Archived,
                    ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    ArchivedById = adminUserId,
                    ClubId = club.ClubId,
                    CreatedById = adminUserId
                },
                new ClubJoinRequestEntity
                {
                    ClubId = club.ClubId,
                    RequestingUserId = applicantUserId,
                    CreatedById = applicantUserId,
                    Status = RequestStatus.Pending
                },
                new ClubActivityEventEntity
                {
                    ClubId = club.ClubId,
                    EventKind = ClubActivityEventKind.CampaignOpened,
                    Audience = ClubActivityAudience.AllMembers,
                    ActorDisplayName = "Bob Observer",
                    CampaignId = undecided.CampaignId,
                    CampaignName = undecided.CampaignName,
                    CreatedById = evaluatorUserId
                });
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        return new SeededDashboardWorkspace(
            club.ClubId,
            adminUserId,
            adminEmail,
            evaluatorUserId,
            evaluatorEmail,
            applicantUserId,
            applicantEmail,
            photoLessEmail,
            clubLessEmail,
            undecided.CampaignId,
            undecided.CampaignName,
            decided.CampaignId,
            decided.Name,
            undecided.CampaignId,
            ActivePlayerCount: 3,
            ArchivedPlayerCount: 1,
            ActiveTeamCount: 2,
            ArchivedTeamCount: 1,
            PendingJoinRequestCount: 1,
            UnresolvedPlacementCount: 2);
    }

    /// <summary>
    /// Seeds a no-campaign club with an administrator and an approved evaluator for the empty-state
    /// browser scenario.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded empty dashboard workspace.</returns>
    public static async Task<SeededEmptyDashboardWorkspace> SeedEmptyClubAsync(
        NovaAppHostFixture fixture,
        CancellationToken cancellationToken)
    {
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("dashboard-empty-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken, firstName: "Erin", lastName: "Empty");
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var evaluatorClient = fixture.CreateNovaHttpClient();
        var evaluatorEmail = SeedingHelpers.UniqueEmail("dashboard-empty-evaluator");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(evaluatorClient, evaluatorEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, evaluatorEmail, club.ClubId, cancellationToken, firstName: "Frank", lastName: "Few");
        await SeedingHelpers.RefreshClubMembershipCookieAsync(evaluatorClient, cancellationToken);

        return new SeededEmptyDashboardWorkspace(club.ClubId, adminEmail, evaluatorEmail);
    }

    /// <summary>
    /// Seeds a second active campaign with a single decided (Not selected) participant, using a newer
    /// season start date than <see cref="SeedingHelpers.SeedCampaignWithParticipantsAsync"/> so card
    /// ordering is deterministic.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminEmail">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded campaign identifier and name.</returns>
    private static async Task<(long CampaignId, string Name)> SeedDecidedCampaignAsync(
        NovaAppHostFixture fixture,
        long clubId,
        string adminEmail,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var adminUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken)).Id;
        var suffix = Guid.NewGuid().ToString("N");
        var name = $"Dash Decided Campaign {suffix}";
        var season = new SeasonEntity { Name = $"Dash Decided Season {suffix}", StartDate = new DateOnly(2026, 2, 1), ClubId = clubId, CreatedById = adminUserId };
        var campaign = new CampaignEntity { Name = name, StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = 0, ClubId = clubId, CreatedById = adminUserId };
        var player = new PlayerEntity { FirstName = "Decided", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = adminUserId };
        context.AddRange(season, campaign, player);
        await context.SaveChangesAsync(cancellationToken);

        context.Add(new PlayerCampaignAssignmentEntity { PlayerId = player.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = adminUserId, PlacementOutcome = PlacementOutcome.NotSelected });
        await context.SaveChangesAsync(cancellationToken);

        return (campaign.CampaignId, name);
    }
}
