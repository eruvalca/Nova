using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Features.Attention;
using Nova.Shared.Features.Dashboard;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Verifies the dashboard summary's authoritative, tenant-scoped counts over HTTP against the
/// Aspire-hosted application: card/roster/team/attention composition, empty-club contracts, and
/// cross-tenant isolation. Activity-feed ordering/translation and the admin-vs-evaluator attention
/// shaping are owned by <c>DashboardQueryPostgresTests</c> and <c>DashboardHttpTests</c> respectively.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class DashboardSummaryHttpTests(NovaAppHostFixture fixture)
{
    /// <summary>Provides the password used by registered integration-test users.</summary>
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies the summary returns authoritative, tenant-scoped counts for the sole Active campaign,
    /// including participant/unresolved counts and the workspace link, plus roster and
    /// team counts. The separate attention projection verifies the administrator counts matching the
    /// seeded pending request and the newest campaign's undecided participants.
    /// </summary>
    [Fact]
    public async Task GetSummary_ReturnsAuthoritativeTenantScopedCounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("dashboard-summary-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var applicantClient = fixture.CreateNovaHttpClient();
        var applicantEmail = SeedingHelpers.UniqueEmail("dashboard-summary-applicant");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(applicantClient, applicantEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, applicantEmail, clubId: null, cancellationToken);

        var suffix = Guid.NewGuid().ToString("N");

        // The manual campaign has the newer season start date, so it sorts first in the card list.
        var manual = await SeedManualCampaignAsync(club.ClubId, adminEmail, suffix, cancellationToken);
        var undecided = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture, club.ClubId, adminEmail, "Dash Undecided", participantCount: 2, PlacementOutcome.Undecided, cancellationToken);

        await using (var context = fixture.CreateAdminContext())
        {
            var adminUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken)).Id;
            var applicantUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == applicantEmail.ToUpperInvariant(), cancellationToken)).Id;

            context.AddRange(
                new PlayerEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    FirstName = "Extra",
                    LastName = "Active",
                    DateOfBirth = new DateOnly(2011, 5, 5),
                    GraduationYear = 2029,
                    LifecycleStatus = LifecycleStatus.Active,
                    ClubId = club.ClubId,
                    CreatedById = adminUserId
                },
                new PlayerEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    FirstName = "Old",
                    LastName = "Player",
                    DateOfBirth = new DateOnly(2008, 5, 5),
                    GraduationYear = 2026,
                    LifecycleStatus = LifecycleStatus.Archived,
                    ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    ArchivedById = adminUserId,
                    ClubId = club.ClubId,
                    CreatedById = adminUserId
                },
                new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = $"Active Team A {suffix}", GraduationYear = 2029, ClubId = club.ClubId, CreatedById = adminUserId },
                new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = $"Active Team B {suffix}", GraduationYear = 2030, ClubId = club.ClubId, CreatedById = adminUserId },
                new TeamEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    Name = $"Archived Team {suffix}",
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
                });
            await context.SaveChangesAsync(cancellationToken);
        }

        using (var response = await adminClient.GetAsync(DashboardEndpoints.GetSummary, cancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var dashboard = await response.Content.ReadFromJsonAsync<ClubDashboardResult>(cancellationToken);
            dashboard.ShouldNotBeNull();

            dashboard.ActiveCampaigns.Count.ShouldBe(1);
            dashboard.ActiveCampaigns[0].Name.ShouldBe(undecided.CampaignName);
            dashboard.ActiveCampaigns[0].ParticipantCount.ShouldBe(2);
            dashboard.ActiveCampaigns[0].UnresolvedCount.ShouldBe(2);
            dashboard.ActiveCampaigns[0].WorkspaceUrl.ShouldBe(DashboardEndpoints.CampaignWorkspaceUrl(undecided.CampaignId));

            dashboard.Roster.ActivePlayers.ShouldBe(4);
            dashboard.Roster.ArchivedPlayers.ShouldBe(1);
            dashboard.Teams.ActiveTeams.ShouldBe(2);
            dashboard.Teams.ArchivedTeams.ShouldBe(1);
        }

        using (var attentionResponse = await adminClient.GetAsync(AttentionEndpoints.GetClubAttention, cancellationToken))
        {
            attentionResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var attention = await attentionResponse.Content.ReadFromJsonAsync<ClubAttentionResult>(cancellationToken);
            attention.ShouldNotBeNull();
            attention.PendingJoinRequests.Status.ShouldBe(AttentionRegionStatus.Loaded);
            attention.PendingJoinRequests.Count.ShouldBe(1);
            attention.NeedsPlacement.Status.ShouldBe(AttentionRegionStatus.Loaded);
            attention.NeedsPlacement.Count.ShouldBe(2);
            attention.NeedsPlacement.CampaignId.ShouldBe(undecided.CampaignId);
            attention.NeedsPlacement.CampaignName.ShouldBe(undecided.CampaignName);
        }
    }

    /// <summary>
    /// Verifies an administrator of a club with no campaigns, players, or teams receives an empty
    /// summary contract with zero counts, and the attention projection reports loaded zero counts.
    /// </summary>
    [Fact]
    public async Task GetSummary_EmptyClub_ReturnsZeroCountsAndEmptyContracts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("dashboard-empty-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken);
        _ = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using (var response = await adminClient.GetAsync(DashboardEndpoints.GetSummary, cancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var dashboard = await response.Content.ReadFromJsonAsync<ClubDashboardResult>(cancellationToken);
            dashboard.ShouldNotBeNull();

            dashboard.ActiveCampaigns.ShouldBeEmpty();
            dashboard.Roster.ActivePlayers.ShouldBe(0);
            dashboard.Roster.ArchivedPlayers.ShouldBe(0);
            dashboard.Teams.ActiveTeams.ShouldBe(0);
            dashboard.Teams.ArchivedTeams.ShouldBe(0);
        }

        using (var attentionResponse = await adminClient.GetAsync(AttentionEndpoints.GetClubAttention, cancellationToken))
        {
            attentionResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var attention = await attentionResponse.Content.ReadFromJsonAsync<ClubAttentionResult>(cancellationToken);
            attention.ShouldNotBeNull();
            attention.PendingJoinRequests.Status.ShouldBe(AttentionRegionStatus.Loaded);
            attention.PendingJoinRequests.Count.ShouldBe(0);
            attention.NeedsPlacement.Status.ShouldBe(AttentionRegionStatus.Loaded);
            attention.NeedsPlacement.Count.ShouldBe(0);
            attention.NeedsPlacement.CampaignId.ShouldBeNull();
        }
    }

    /// <summary>
    /// Verifies an evaluator of an empty club receives zero counts from the summary, and is
    /// forbidden from the administrator-only attention projection even when the club has no data.
    /// </summary>
    [Fact]
    public async Task GetSummary_EmptyClub_EvaluatorOmitsAttention()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("dashboard-empty-eval-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var evaluatorClient = fixture.CreateNovaHttpClient();
        var evaluatorEmail = SeedingHelpers.UniqueEmail("dashboard-empty-evaluator");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(evaluatorClient, evaluatorEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, evaluatorEmail, club.ClubId, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(evaluatorClient, cancellationToken);

        using (var response = await evaluatorClient.GetAsync(DashboardEndpoints.GetSummary, cancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var dashboard = await response.Content.ReadFromJsonAsync<ClubDashboardResult>(cancellationToken);
            dashboard.ShouldNotBeNull();

            dashboard.ActiveCampaigns.ShouldBeEmpty();
            dashboard.Roster.ActivePlayers.ShouldBe(0);
            dashboard.Roster.ArchivedPlayers.ShouldBe(0);
            dashboard.Teams.ActiveTeams.ShouldBe(0);
            dashboard.Teams.ArchivedTeams.ShouldBe(0);
        }

        using (var attentionResponse = await evaluatorClient.GetAsync(AttentionEndpoints.GetClubAttention, cancellationToken))
        {
            attentionResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    /// <summary>
    /// Verifies the summary, attention, and activity reads are tenant-isolated: decoy rows in a second
    /// club are never surfaced to the first club's administrator in card names, roster/team counts,
    /// attention counts, or activity results.
    /// </summary>
    [Fact]
    public async Task GetSummary_IsTenantIsolated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var adminAClient = fixture.CreateNovaHttpClient();
        var adminAEmail = SeedingHelpers.UniqueEmail("dashboard-tenant-a-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminAClient, adminAEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminAEmail, clubId: null, cancellationToken);
        var clubA = await SeedingHelpers.CreateClubAsync(adminAClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminAClient, cancellationToken);

        using var adminBClient = fixture.CreateNovaHttpClient();
        var adminBEmail = SeedingHelpers.UniqueEmail("dashboard-tenant-b-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminBClient, adminBEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminBEmail, clubId: null, cancellationToken);
        var clubB = await SeedingHelpers.CreateClubAsync(adminBClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminBClient, cancellationToken);

        var suffix = Guid.NewGuid().ToString("N");
        var clubACampaignName = $"Club A Campaign {suffix}";
        var clubBCampaignName = $"Club B Decoy {suffix}";

        await using (var context = fixture.CreateAdminContext())
        {
            var adminAUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == adminAEmail.ToUpperInvariant(), cancellationToken)).Id;
            var adminBUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == adminBEmail.ToUpperInvariant(), cancellationToken)).Id;

            // Club A: one active campaign, one active player, one active team, and one note on its assignment.
            var seasonA = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = $"Club A Season {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = clubA.ClubId, CreatedById = adminAUserId };
            var campaignA = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = clubACampaignName, StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = seasonA, SeasonId = 0, ClubId = clubA.ClubId, CreatedById = adminAUserId };
            var playerA = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Club", LastName = "APlayer", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubA.ClubId, CreatedById = adminAUserId };
            var teamA = new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = $"Club A Team {suffix}", GraduationYear = 2028, ClubId = clubA.ClubId, CreatedById = adminAUserId };
            context.AddRange(seasonA, campaignA, playerA, teamA);
            await context.SaveChangesAsync(cancellationToken);

            var assignmentA = new PlayerCampaignAssignmentEntity { PlayerId = playerA.PlayerId, CampaignId = campaignA.CampaignId, ClubId = clubA.ClubId, CreatedById = adminAUserId, PlacementOutcome = PlacementOutcome.Assigned, TeamId = teamA.TeamId };
            context.Add(assignmentA);
            await context.SaveChangesAsync(cancellationToken);
            context.Add(new NoteEntity { CreationOperationId = Guid.NewGuid(), Content = "Club A note", PlayerCampaignAssignmentId = assignmentA.PlayerCampaignAssignmentId, ClubId = clubA.ClubId, CreatedById = adminAUserId });
            context.Add(new ActivityEventEntity
            {
                ClubId = clubA.ClubId,
                EventKind = ActivityEventKind.CampaignOpened,
                CampaignId = campaignA.CampaignId,
                ActorUserId = adminAUserId,
                ActorDisplayName = "Club A Admin",
                PayloadJson = JsonSerializer.Serialize(
                    new CampaignLifecycleContext
                    {
                        CampaignId = campaignA.CampaignId,
                        CampaignName = clubACampaignName
                    },
                    typeof(ClubActivityContext)),
                CreatedById = adminAUserId
            });

            // Club B: decoy campaign, players, archived team, and a pending join request.
            var seasonB = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = $"Club B Season {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = clubB.ClubId, CreatedById = adminBUserId };
            var campaignB = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = clubBCampaignName, StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = seasonB, SeasonId = 0, ClubId = clubB.ClubId, CreatedById = adminBUserId };
            context.AddRange(
                seasonB,
                campaignB,
                new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Club", LastName = "BPlayer1", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubB.ClubId, CreatedById = adminBUserId },
                new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Club", LastName = "BPlayer2", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubB.ClubId, CreatedById = adminBUserId },
                new TeamEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    Name = $"Club B Archived Team {suffix}",
                    GraduationYear = 2027,
                    LifecycleStatus = LifecycleStatus.Archived,
                    ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    ArchivedById = adminBUserId,
                    ClubId = clubB.ClubId,
                    CreatedById = adminBUserId
                },
                new ClubJoinRequestEntity { ClubId = clubB.ClubId, RequestingUserId = adminAUserId, CreatedById = adminAUserId, Status = RequestStatus.Pending },
                new ActivityEventEntity
                {
                    ClubId = clubB.ClubId,
                    EventKind = ActivityEventKind.CampaignOpened,
                    CampaignId = campaignB.CampaignId,
                    ActorUserId = adminBUserId,
                    ActorDisplayName = "Club B Admin",
                    PayloadJson = JsonSerializer.Serialize(
                        new CampaignLifecycleContext
                        {
                            CampaignId = campaignB.CampaignId,
                            CampaignName = clubBCampaignName
                        },
                        typeof(ClubActivityContext)),
                    CreatedById = adminBUserId
                });
            await context.SaveChangesAsync(cancellationToken);
        }

        using (var response = await adminAClient.GetAsync(DashboardEndpoints.GetSummary, cancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var dashboard = await response.Content.ReadFromJsonAsync<ClubDashboardResult>(cancellationToken);
            dashboard.ShouldNotBeNull();

            dashboard.ActiveCampaigns.Select(card => card.Name).ShouldBe([clubACampaignName]);
            dashboard.Roster.ActivePlayers.ShouldBe(1);
            dashboard.Roster.ArchivedPlayers.ShouldBe(0);
            dashboard.Teams.ActiveTeams.ShouldBe(1);
            dashboard.Teams.ArchivedTeams.ShouldBe(0);
        }

        using (var attentionResponse = await adminAClient.GetAsync(AttentionEndpoints.GetClubAttention, cancellationToken))
        {
            attentionResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var attention = await attentionResponse.Content.ReadFromJsonAsync<ClubAttentionResult>(cancellationToken);
            attention.ShouldNotBeNull();
            attention.PendingJoinRequests.Status.ShouldBe(AttentionRegionStatus.Loaded);
            attention.PendingJoinRequests.Count.ShouldBe(0);
            attention.NeedsPlacement.Status.ShouldBe(AttentionRegionStatus.Loaded);
            attention.NeedsPlacement.Count.ShouldBe(0);
        }

        using (var activityResponse = await adminAClient.GetAsync(ActivityEndpoints.GetClubActivity, cancellationToken))
        {
            activityResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var activity = await activityResponse.Content.ReadFromJsonAsync<ClubActivityResult>(cancellationToken);
            activity.ShouldNotBeNull();

            activity.Events.ShouldNotBeEmpty();
            activity.Events.Select(item => item.Context).OfType<CampaignLifecycleContext>()
                .Select(context => context.CampaignName)
                .ShouldContain(clubACampaignName);
            activity.Events.Select(item => item.Context).OfType<CampaignLifecycleContext>()
                .Select(context => context.CampaignName)
                .ShouldNotContain(clubBCampaignName);
        }
    }

    /// <summary>
    /// Seeds one active campaign with a single undecided participant for the given club, using a newer
    /// season start date than the shared <see cref="SeedingHelpers.SeedCampaignWithParticipantsAsync"/>
    /// helper so card ordering is deterministic.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminEmail">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="suffix">A stable name suffix.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded campaign identifier and name.</returns>
    private async Task<(long CampaignId, string Name)> SeedManualCampaignAsync(
        long clubId,
        string adminEmail,
        string suffix,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var adminUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken)).Id;
        var name = $"Manual Campaign {suffix}";
        var season = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = $"Manual Season {suffix}", StartDate = new DateOnly(2026, 2, 1), ClubId = clubId, CreatedById = adminUserId };
        var campaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = name, StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = 0, ClubId = clubId, CreatedById = adminUserId };
        var player = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Manual", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = adminUserId };
        context.AddRange(season, campaign, player);
        await context.SaveChangesAsync(cancellationToken);

        context.Add(new PlayerCampaignAssignmentEntity { PlayerId = player.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = adminUserId, PlacementOutcome = PlacementOutcome.Undecided });
        await context.SaveChangesAsync(cancellationToken);

        return (campaign.CampaignId, name);
    }
}
