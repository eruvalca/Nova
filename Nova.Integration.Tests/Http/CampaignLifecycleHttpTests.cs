using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Shouldly;
using static Nova.Integration.Tests.Http.SeedingHelpers;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the campaign close and reopen endpoints.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignLifecycleHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies anonymous callers receive an unauthorized response for both lifecycle endpoints.
    /// </summary>
    [Fact]
    public async Task CampaignLifecycle_ReturnsUnauthorized_ForAnonymousCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using var closeResponse = await anonymousClient.PostAsync(
            CampaignEndpoints.CloseUrl(1),
            content: null,
            cancellationToken);
        closeResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var reopenResponse = await anonymousClient.PostAsync(
            CampaignEndpoints.ReopenUrl(1),
            content: null,
            cancellationToken);
        reopenResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies an authenticated club member without administrator rights receives a forbidden response
    /// for both lifecycle endpoints.
    /// </summary>
    [Fact]
    public async Task CampaignLifecycle_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("lifecycle-member-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);
        var seeded = await SeedCampaignAsync(
            club.ClubId,
            adminEmail,
            "lifecycle-member",
            [ReadyParticipant],
            cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("lifecycle-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(fixture, memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        using var closeResponse = await memberClient.PostAsync(
            CampaignEndpoints.CloseUrl(seeded.CampaignId),
            content: null,
            cancellationToken);
        closeResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var reopenResponse = await memberClient.PostAsync(
            CampaignEndpoints.ReopenUrl(seeded.CampaignId),
            content: null,
            cancellationToken);
        reopenResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a club administrator can close a fully-decided campaign, persisting closure metadata
    /// and a Closed lifecycle event transactionally.
    /// </summary>
    [Fact]
    public async Task CampaignClose_ReturnsNoContent_AndPersistsClosure_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("lifecycle-close-success");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client, email, Password, cancellationToken);
        await UpdateUserAsync(fixture, email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedCampaignAsync(
            club.ClubId,
            email,
            "lifecycle-close-success",
            [ReadyParticipant],
            cancellationToken);

        using var response = await client.PostAsync(
            CampaignEndpoints.CloseUrl(seeded.CampaignId),
            content: null,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var context = fixture.CreateAdminContext();
        var campaign = await context.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seeded.CampaignId, cancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Closed);
        campaign.ClosedAt.ShouldNotBeNull();
        campaign.ClosedById.ShouldBe(seeded.AdminUserId);

        var events = await context.CampaignLifecycleEvents
            .Where(candidate => candidate.CampaignId == seeded.CampaignId)
            .OrderBy(candidate => candidate.CampaignLifecycleEventId)
            .ToListAsync(cancellationToken);
        events.Count.ShouldBe(1);
        events[0].EventType.ShouldBe(CampaignLifecycleEventType.Closed);
        events[0].ClubId.ShouldBe(club.ClubId);
        events[0].CreatedById.ShouldBe(seeded.AdminUserId);
    }

    /// <summary>
    /// Verifies close returns all condition-keyed blockers and leaves the campaign active when
    /// participants are undecided, ineligible, or assigned to an archived team.
    /// </summary>
    [Fact]
    public async Task CampaignClose_ReturnsConflict_WithConditionKeyedBlockers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("lifecycle-close-blocked");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client, email, Password, cancellationToken);
        await UpdateUserAsync(fixture, email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedCampaignAsync(
            club.ClubId,
            email,
            "lifecycle-close-blocked",
            [
                new CampaignParticipantSpec(PlacementOutcome.Undecided, PlayerGraduationYear: 2030),
                new CampaignParticipantSpec(
                    PlacementOutcome.Assigned,
                    PlayerGraduationYear: 2030,
                    TeamGraduationYear: 2031),
                new CampaignParticipantSpec(
                    PlacementOutcome.Assigned,
                    PlayerGraduationYear: 2030,
                    TeamGraduationYear: 2029,
                    TeamLifecycleStatus: LifecycleStatus.Archived)
            ],
            cancellationToken);
        var undecidedId = seeded.Participants[0].AssignmentId;
        var ineligibleId = seeded.Participants[1].AssignmentId;
        var archivedTeamId = seeded.Participants[2].AssignmentId;

        using var response = await client.PostAsync(
            CampaignEndpoints.CloseUrl(seeded.CampaignId),
            content: null,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var errors = await ReadErrorsAsync(response, cancellationToken);
        errors.ShouldContainKey("outcomes");
        errors.ShouldContainKey("eligibility");
        errors.ShouldContainKey("archivedTeams");
        errors["outcomes"].Single().ShouldContain("1 undecided participation record");
        errors["eligibility"].Single().ShouldContain(ineligibleId.ToString());
        errors["archivedTeams"].Single().ShouldContain(archivedTeamId.ToString());

        await using var context = fixture.CreateAdminContext();
        var campaign = await context.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seeded.CampaignId, cancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.ClosedAt.ShouldBeNull();
        campaign.ClosedById.ShouldBeNull();
        (await context.CampaignLifecycleEvents
            .AnyAsync(candidate => candidate.CampaignId == seeded.CampaignId, cancellationToken))
            .ShouldBeFalse();
    }

    /// <summary>
    /// Verifies another club's campaign is hidden behind a non-disclosing not-found response.
    /// </summary>
    [Fact]
    public async Task CampaignClose_ReturnsNotFound_ForCrossTenantCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var ownerClient = fixture.CreateNovaHttpClient();
        var ownerEmail = UniqueEmail("lifecycle-cross-owner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            ownerClient, ownerEmail, Password, cancellationToken);
        await UpdateUserAsync(fixture, ownerEmail, clubId: null, cancellationToken);
        var ownerClub = await CreateClubAsync(ownerClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(ownerClient, cancellationToken);
        var seeded = await SeedCampaignAsync(
            ownerClub.ClubId,
            ownerEmail,
            "lifecycle-cross",
            [ReadyParticipant],
            cancellationToken);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("lifecycle-cross-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(fixture, otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        otherClub.ClubId.ShouldNotBe(ownerClub.ClubId);

        using var response = await otherClient.PostAsync(
            CampaignEndpoints.CloseUrl(seeded.CampaignId),
            content: null,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Verifies closing an already-closed campaign and reopening an already-active campaign both conflict.
    /// </summary>
    [Fact]
    public async Task CampaignLifecycle_ReturnsConflict_ForAlreadyTransitionedCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("lifecycle-already");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client, email, Password, cancellationToken);
        await UpdateUserAsync(fixture, email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        var closedCampaign = await SeedCampaignAsync(
            club.ClubId,
            email,
            "lifecycle-already-closed",
            [ReadyParticipant],
            cancellationToken,
            closed: true);
        using var closeAgainResponse = await client.PostAsync(
            CampaignEndpoints.CloseUrl(closedCampaign.CampaignId),
            content: null,
            cancellationToken);
        closeAgainResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var activeCampaign = await SeedCampaignAsync(
            club.ClubId,
            email,
            "lifecycle-already-active",
            [ReadyParticipant],
            cancellationToken);
        using var reopenActiveResponse = await client.PostAsync(
            CampaignEndpoints.ReopenUrl(activeCampaign.CampaignId),
            content: null,
            cancellationToken);
        reopenActiveResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Verifies a club administrator can reopen a closed campaign, clearing closure metadata and
    /// persisting a Reopened lifecycle event.
    /// </summary>
    [Fact]
    public async Task CampaignReopen_ReturnsNoContent_AndClearsClosure_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("lifecycle-reopen-success");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client, email, Password, cancellationToken);
        await UpdateUserAsync(fixture, email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedCampaignAsync(
            club.ClubId,
            email,
            "lifecycle-reopen-success",
            [ReadyParticipant],
            cancellationToken,
            closed: true);

        using var response = await client.PostAsync(
            CampaignEndpoints.ReopenUrl(seeded.CampaignId),
            content: null,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var context = fixture.CreateAdminContext();
        var campaign = await context.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == seeded.CampaignId, cancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.ClosedAt.ShouldBeNull();
        campaign.ClosedById.ShouldBeNull();

        var events = await context.CampaignLifecycleEvents
            .Where(candidate => candidate.CampaignId == seeded.CampaignId)
            .OrderBy(candidate => candidate.CampaignLifecycleEventId)
            .Select(candidate => candidate.EventType)
            .ToListAsync(cancellationToken);
        events.ShouldBe(
        [
            CampaignLifecycleEventType.Closed,
            CampaignLifecycleEventType.Reopened
        ]);
    }

    /// <summary>
    /// Verifies that reopening a closed campaign restores placement editing without discarding the
    /// previously decided outcomes.
    /// </summary>
    [Fact]
    public async Task CampaignReopen_RestoresEditing_WithoutDiscardingOutcomes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("lifecycle-reopen-restores");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client, email, Password, cancellationToken);
        await UpdateUserAsync(fixture, email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedCampaignAsync(
            club.ClubId,
            email,
            "lifecycle-reopen-restores",
            [
                new CampaignParticipantSpec(PlacementOutcome.Assigned, PlayerGraduationYear: 2030, TeamGraduationYear: 2029),
                new CampaignParticipantSpec(PlacementOutcome.NotSelected, PlayerGraduationYear: 2030)
            ],
            cancellationToken);

        using (var closeResponse = await client.PostAsync(
                   CampaignEndpoints.CloseUrl(seeded.CampaignId), content: null, cancellationToken))
        {
            closeResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var reopenResponse = await client.PostAsync(
                   CampaignEndpoints.ReopenUrl(seeded.CampaignId), content: null, cancellationToken))
        {
            reopenResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var previouslyNotSelected = seeded.Participants[1];
        var eligibleTeamId = seeded.Participants[0].TeamId!.Value;

        Guid expectedToken;
        await using (var context = fixture.CreateAdminContext())
        {
            expectedToken = await context.PlayerCampaignAssignments
                .Where(assignment => assignment.PlayerCampaignAssignmentId == previouslyNotSelected.AssignmentId)
                .Select(assignment => assignment.ConcurrencyToken)
                .SingleAsync(cancellationToken);
        }

        using var updateResponse = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(previouslyNotSelected.AssignmentId),
            new UpdateCampaignPlacementInput(
                previouslyNotSelected.AssignmentId,
                PlacementOutcome.Assigned,
                eligibleTeamId,
                expectedToken),
            cancellationToken);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var verify = fixture.CreateAdminContext();
        var updated = await verify.PlayerCampaignAssignments
            .SingleAsync(
                assignment => assignment.PlayerCampaignAssignmentId == previouslyNotSelected.AssignmentId,
                cancellationToken);
        updated.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        updated.TeamId.ShouldBe(eligibleTeamId);

        var untouched = await verify.PlayerCampaignAssignments
            .SingleAsync(
                assignment => assignment.PlayerCampaignAssignmentId == seeded.Participants[0].AssignmentId,
                cancellationToken);
        untouched.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        untouched.TeamId.ShouldBe(eligibleTeamId);

        var reopenEvents = await verify.CampaignLifecycleEvents
            .Where(candidate => candidate.CampaignId == seeded.CampaignId)
            .OrderBy(candidate => candidate.CampaignLifecycleEventId)
            .Select(candidate => new { candidate.EventType, candidate.CreatedById })
            .ToListAsync(cancellationToken);
        reopenEvents.Last().EventType.ShouldBe(CampaignLifecycleEventType.Reopened);
        reopenEvents.Last().CreatedById.ShouldBe(seeded.AdminUserId);
    }

    /// <summary>
    /// Verifies another club's closed campaign is hidden behind a non-disclosing not-found response
    /// when a club administrator attempts to reopen it.
    /// </summary>
    [Fact]
    public async Task CampaignReopen_ReturnsNotFound_ForCrossTenantCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var ownerClient = fixture.CreateNovaHttpClient();
        var ownerEmail = UniqueEmail("lifecycle-reopen-cross-owner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            ownerClient, ownerEmail, Password, cancellationToken);
        await UpdateUserAsync(fixture, ownerEmail, clubId: null, cancellationToken);
        var ownerClub = await CreateClubAsync(ownerClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(ownerClient, cancellationToken);
        var seeded = await SeedCampaignAsync(
            ownerClub.ClubId,
            ownerEmail,
            "lifecycle-reopen-cross",
            [ReadyParticipant],
            cancellationToken,
            closed: true);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("lifecycle-reopen-cross-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(fixture, otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        otherClub.ClubId.ShouldNotBe(ownerClub.ClubId);

        using var response = await otherClient.PostAsync(
            CampaignEndpoints.ReopenUrl(seeded.CampaignId),
            content: null,
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.TryGetProperty("detail", out _).ShouldBeFalse();
    }

    /// <summary>
    /// Defines one seeded campaign participant's placement facts.
    /// </summary>
    /// <param name="Outcome">The placement outcome.</param>
    /// <param name="PlayerGraduationYear">The player's graduation year.</param>
    /// <param name="TeamGraduationYear">The assigned team's graduation year, when assigned.</param>
    /// <param name="TeamLifecycleStatus">The assigned team's lifecycle status, when assigned.</param>
    private sealed record CampaignParticipantSpec(
        PlacementOutcome Outcome,
        int PlayerGraduationYear,
        int? TeamGraduationYear = null,
        LifecycleStatus? TeamLifecycleStatus = null);

    /// <summary>
    /// A fully-decided, eligible participant used for close/reopen success scenarios.
    /// </summary>
    private static CampaignParticipantSpec ReadyParticipant =>
        new(PlacementOutcome.Assigned, PlayerGraduationYear: 2030, TeamGraduationYear: 2029);

    /// <summary>
    /// The identifiers produced by campaign lifecycle seeding.
    /// </summary>
    /// <param name="CampaignId">The campaign identifier.</param>
    /// <param name="AdminUserId">The seeding administrator's user identifier.</param>
    /// <param name="Participants">The seeded participant assignment identifiers.</param>
    private sealed record CampaignSeed(
        long CampaignId,
        long AdminUserId,
        IReadOnlyList<SeededParticipant> Participants);

    /// <summary>
    /// The identifiers for one seeded campaign participation.
    /// </summary>
    /// <param name="AssignmentId">The campaign participation identifier.</param>
    /// <param name="Outcome">The seeded placement outcome.</param>
    /// <param name="TeamId">The assigned team identifier, when assigned.</param>
    private sealed record SeededParticipant(long AssignmentId, PlacementOutcome Outcome, long? TeamId);

    /// <summary>
    /// Seeds a campaign with the requested participant graph through the admin context.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminEmail">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="namePrefix">A stable name prefix for the seeded records.</param>
    /// <param name="participants">The participants to seed.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <param name="closed">Whether the campaign should be seeded as closed.</param>
    /// <returns>The seeded campaign, admin user, and participant identifiers.</returns>
    private async Task<CampaignSeed> SeedCampaignAsync(
        long clubId,
        string adminEmail,
        string namePrefix,
        IReadOnlyList<CampaignParticipantSpec> participants,
        CancellationToken cancellationToken,
        bool closed = false)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(
            candidate => candidate.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken);

        var suffix = Guid.NewGuid().ToString("N");
        var season = new SeasonEntity
        {
            Name = $"{namePrefix} Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubId,
            CreatedById = user.Id
        };
        var campaign = new CampaignEntity
        {
            Name = $"{namePrefix} Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = closed ? CampaignStatus.Closed : CampaignStatus.Active,
            ClosedAt = closed ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            ClosedById = closed ? user.Id : null,
            Season = season,
            SeasonId = 0,
            ClubId = clubId,
            CreatedById = user.Id
        };
        context.AddRange(season, campaign);
        await context.SaveChangesAsync(cancellationToken);

        var players = new List<PlayerEntity>(participants.Count);
        var teams = new List<TeamEntity?>(participants.Count);
        for (var index = 0; index < participants.Count; index++)
        {
            var spec = participants[index];
            players.Add(new PlayerEntity
            {
                FirstName = namePrefix,
                LastName = $"Player {index + 1:D2}",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = spec.PlayerGraduationYear,
                LifecycleStatus = LifecycleStatus.Active,
                ClubId = clubId,
                CreatedById = user.Id
            });

            teams.Add(spec.TeamGraduationYear is int teamGraduationYear
                ? new TeamEntity
                {
                    Name = $"{namePrefix} Team {index + 1:D2} {suffix}",
                    GraduationYear = teamGraduationYear,
                    LifecycleStatus = spec.TeamLifecycleStatus ?? LifecycleStatus.Active,
                    ArchivedAt = spec.TeamLifecycleStatus == LifecycleStatus.Archived
                        ? DateTimeOffset.UtcNow.AddDays(-1)
                        : null,
                    ArchivedById = spec.TeamLifecycleStatus == LifecycleStatus.Archived ? user.Id : null,
                    ClubId = clubId,
                    CreatedById = user.Id
                }
                : null);
        }

        context.AddRange(players);
        context.AddRange(teams.Where(team => team is not null).Select(team => team!));
        await context.SaveChangesAsync(cancellationToken);

        var seededParticipants = new List<SeededParticipant>(participants.Count);
        for (var index = 0; index < participants.Count; index++)
        {
            var spec = participants[index];
            var teamId = teams[index]?.TeamId;
            var assignment = new PlayerCampaignAssignmentEntity
            {
                PlayerId = players[index].PlayerId,
                CampaignId = campaign.CampaignId,
                ClubId = clubId,
                CreatedById = user.Id,
                PlacementOutcome = spec.Outcome,
                TeamId = teamId,
                TryoutNumber = index + 1
            };
            context.Add(assignment);
            await context.SaveChangesAsync(cancellationToken);
            seededParticipants.Add(new SeededParticipant(
                assignment.PlayerCampaignAssignmentId,
                spec.Outcome,
                teamId));
        }

        if (closed)
        {
            context.CampaignLifecycleEvents.Add(new CampaignLifecycleEventEntity
            {
                CampaignId = campaign.CampaignId,
                EventType = CampaignLifecycleEventType.Closed,
                ClubId = clubId,
                CreatedById = user.Id
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        return new CampaignSeed(campaign.CampaignId, user.Id, seededParticipants);
    }

    /// <summary>
    /// Reads the <c>errors</c> dictionary from a ProblemDetails payload.
    /// </summary>
    /// <param name="response">The problem-details response.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The structured error dictionary.</returns>
    private static async Task<Dictionary<string, string[]>> ReadErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var errors = document.RootElement.GetProperty("errors");
        return errors.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray());
    }
}
