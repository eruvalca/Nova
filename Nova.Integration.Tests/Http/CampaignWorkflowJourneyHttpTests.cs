using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Players;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Full HTTP journeys across the six primary campaign workflows: creation, late-player enrollment,
/// evaluation, placement, close, and reopen. Every workflow step is driven through the real HTTP API
/// (<see cref="CampaignEndpoints"/> / <see cref="PlayerEndpoints"/>); EF is used only for
/// identity/club prerequisites and final persisted-state assertions via
/// <see cref="NovaAppHostFixture.CreateAdminContext"/>. This file intentionally does not re-assert
/// authorization, tenancy, or pure-policy decision rules — those matrices are owned by sibling
/// sub-issues of epic #13.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignWorkflowJourneyHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    // ── Phase 1: creation and late-enrollment journeys ─────────────────────────

    /// <summary>
    /// A club admin creating an Active campaign after two players already exist receives a
    /// <see cref="CreateCampaignResult"/> reporting both players auto-enrolled, and the roster plus
    /// persisted participation rows confirm two undecided assignments.
    /// </summary>
    [Fact]
    public async Task CreationJourney_AutoEnrollsPreExistingActivePlayers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(adminClient, "journey-creation", cancellationToken);

        var firstPlayer = await CreatePlayerViaHttpAsync(adminClient, ValidCreatePlayerInput($"First {Guid.CreateVersion7():N}"), cancellationToken);
        var secondPlayer = await CreatePlayerViaHttpAsync(adminClient, ValidCreatePlayerInput($"Second {Guid.CreateVersion7():N}"), cancellationToken);

        var created = await CreateCampaignViaHttpAsync(adminClient, cancellationToken);
        created.Status.ShouldBe(CampaignStatus.Active);
        created.EnrolledPlayerCount.ShouldBe(2);

        var roster = await GetParticipantRosterAsync(adminClient, created.CampaignId, cancellationToken);
        roster.TotalCount.ShouldBe(2);
        roster.Items.Select(item => item.PlayerId).OrderBy(id => id)
            .ShouldBe(new[] { firstPlayer.PlayerId, secondPlayer.PlayerId }.OrderBy(id => id));

        await using var context = fixture.CreateAdminContext();
        var assignments = await context.PlayerCampaignAssignments
            .Where(assignment => assignment.CampaignId == created.CampaignId)
            .OrderBy(assignment => assignment.PlayerId)
            .ToListAsync(cancellationToken);
        assignments.Count.ShouldBe(2);
        assignments.ShouldAllBe(assignment => assignment.PlacementOutcome == PlacementOutcome.Undecided);
        // The HTTP auto-enrollment path does not assign tryout numbers.
        assignments.ShouldAllBe(assignment => assignment.TryoutNumber == null);
    }

    /// <summary>
    /// Creating a player after the campaign exists auto-enrolls the new player into the Active
    /// campaign: the roster and participant detail are reachable, and a participation row persists.
    /// </summary>
    [Fact]
    public async Task LateEnrollmentJourney_NewPlayerEntersActiveCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(adminClient, "journey-late-enrollment", cancellationToken);

        var created = await CreateCampaignViaHttpAsync(adminClient, cancellationToken);
        created.EnrolledPlayerCount.ShouldBe(0);

        var player = await CreatePlayerViaHttpAsync(adminClient, ValidCreatePlayerInput($"Late {Guid.CreateVersion7():N}"), cancellationToken);

        var roster = await GetParticipantRosterAsync(adminClient, created.CampaignId, cancellationToken);
        var rosterItem = roster.Items.ShouldHaveSingleItem();
        rosterItem.PlayerId.ShouldBe(player.PlayerId);
        rosterItem.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);

        await using var context = fixture.CreateAdminContext();
        var assignment = await context.PlayerCampaignAssignments
            .SingleOrDefaultAsync(
                candidate => candidate.PlayerId == player.PlayerId && candidate.CampaignId == created.CampaignId,
                cancellationToken);
        assignment.ShouldNotBeNull("the new player should have a participation row after creation");

        var detail = await GetParticipantDetailAsync(adminClient, created.CampaignId, rosterItem.PlayerCampaignAssignmentId, cancellationToken);
        detail.PlayerId.ShouldBe(player.PlayerId);
    }

    // ── Phase 2: evaluation and placement journeys (multi-user) ────────────────

    /// <summary>
    /// An evaluator's evaluation note on an HTTP-created participant is visible to the administrator
    /// with the evaluator's actor metadata, and the evaluator's subsequent edit is observed by the
    /// administrator through the participant detail payload.
    /// </summary>
    [Fact]
    public async Task EvaluationJourney_EvaluatorNote_IsConsumedByAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "journey-eval", cancellationToken);
        var evaluatorClient = await RegisterClubEvaluatorAsync("journey-eval", admin.Club.ClubId, "Eva", "Evaluator", cancellationToken);

        var created = await CreateCampaignViaHttpAsync(adminClient, cancellationToken);
        var player = await CreatePlayerViaHttpAsync(adminClient, ValidCreatePlayerInput($"Eval {Guid.CreateVersion7():N}"), cancellationToken);
        var assignmentId = await GetSingleAssignmentIdAsync(created.CampaignId, player.PlayerId, cancellationToken);

        using (var addResponse = await evaluatorClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            new AddEvaluationNoteInput { PlayerCampaignAssignmentId = assignmentId, Content = "Strong first touch." },
            cancellationToken))
        {
            addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
            var added = await addResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(cancellationToken);
            added.NoteId.ShouldBeGreaterThan(0);
        }

        var detail = await GetParticipantDetailAsync(adminClient, created.CampaignId, assignmentId, cancellationToken);
        var note = detail.Notes.ShouldHaveSingleItem();
        note.Content.ShouldBe("Strong first touch.");
        note.AuthorDisplayName.ShouldBe("Eva Evaluator");

        using (var editResponse = await evaluatorClient.PutAsJsonAsync(
            CampaignEndpoints.EditEvaluationNoteUrl(detail.Notes[0].NoteId),
            new PutEvaluationNoteInput { Content = "Refined after the second drill." },
            cancellationToken))
        {
            editResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var updatedDetail = await GetParticipantDetailAsync(adminClient, created.CampaignId, assignmentId, cancellationToken);
        updatedDetail.Notes.ShouldHaveSingleItem().Content.ShouldBe("Refined after the second drill.");
    }

    /// <summary>
    /// Two chained placement updates — Undecided → NotSelected → Assigned — each return a fresh
    /// replacement token, and both the placement roster and summary plus the persisted assignment row
    /// reflect the final outcome and team.
    /// </summary>
    [Fact]
    public async Task PlacementJourney_ReplacementTokenChain_UpdatesRosterAndSummary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        _ = await RegisterClubAdminAsync(adminClient, "journey-placement", cancellationToken);

        var team = await CreateTeamViaHttpAsync(adminClient, graduationYear: 2029, cancellationToken);
        var created = await CreateCampaignViaHttpAsync(adminClient, cancellationToken);
        var player = await CreatePlayerViaHttpAsync(adminClient, ValidCreatePlayerInput($"Placement {Guid.CreateVersion7():N}"), cancellationToken);
        var assignmentId = await GetSingleAssignmentIdAsync(created.CampaignId, player.PlayerId, cancellationToken);

        var initialRoster = await GetPlacementRosterAsync(adminClient, created.CampaignId, cancellationToken);
        var initialItem = initialRoster.Items.ShouldHaveSingleItem();
        initialItem.PlayerCampaignAssignmentId.ShouldBe(assignmentId);
        initialItem.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);

        Guid firstToken;
        using (var firstResponse = await adminClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.NotSelected, teamId: null, initialItem.ConcurrencyToken),
            cancellationToken))
        {
            firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var first = await firstResponse.Content.ReadFromJsonAsync<PlacementMutationSuccess>(cancellationToken);
            firstToken = first.ConcurrencyToken;
            firstToken.ShouldNotBe(Guid.Empty);
            firstToken.ShouldNotBe(initialItem.ConcurrencyToken);
        }

        var notSelectedRoster = await GetPlacementRosterAsync(adminClient, created.CampaignId, cancellationToken);
        var notSelectedItem = notSelectedRoster.Items.ShouldHaveSingleItem();
        notSelectedItem.PlacementOutcome.ShouldBe(PlacementOutcome.NotSelected);
        notSelectedItem.Team.ShouldBeNull();
        var notSelectedSummary = await GetPlacementSummaryAsync(adminClient, created.CampaignId, cancellationToken);
        notSelectedSummary.TotalCount.ShouldBe(1);
        notSelectedSummary.NotSelectedCount.ShouldBe(1);
        notSelectedSummary.AssignedCount.ShouldBe(0);

        Guid secondToken;
        using (var secondResponse = await adminClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, team.TeamId, firstToken),
            cancellationToken))
        {
            secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var second = await secondResponse.Content.ReadFromJsonAsync<PlacementMutationSuccess>(cancellationToken);
            secondToken = second.ConcurrencyToken;
            secondToken.ShouldNotBe(Guid.Empty);
            secondToken.ShouldNotBe(firstToken);
        }

        var assignedRoster = await GetPlacementRosterAsync(adminClient, created.CampaignId, cancellationToken);
        var assignedItem = assignedRoster.Items.ShouldHaveSingleItem();
        assignedItem.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        assignedItem.Team.ShouldNotBeNull();
        assignedItem.Team!.TeamId.ShouldBe(team.TeamId);
        var assignedSummary = await GetPlacementSummaryAsync(adminClient, created.CampaignId, cancellationToken);
        assignedSummary.TotalCount.ShouldBe(1);
        assignedSummary.AssignedCount.ShouldBe(1);
        assignedSummary.NotSelectedCount.ShouldBe(0);

        await using var context = fixture.CreateAdminContext();
        var persisted = await context.PlayerCampaignAssignments
            .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        persisted.TeamId.ShouldBe(team.TeamId);
        persisted.ConcurrencyToken.ShouldBe(secondToken);
    }

    // ── Phase 3: close/reopen journeys and verified concurrency ────────────────

    /// <summary>
    /// A campaign with an undecided assignment reports blocked readiness; once the assignment is
    /// placed and the campaign closed, the evaluator retains read access while all writes conflict.
    /// </summary>
    [Fact]
    public async Task CloseJourney_ReadinessToClosed_EvaluatorReadSurfacePreserved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "journey-close", cancellationToken);
        var evaluatorClient = await RegisterClubEvaluatorAsync("journey-close", admin.Club.ClubId, "Casey", "Evaluator", cancellationToken);

        var created = await CreateCampaignViaHttpAsync(adminClient, cancellationToken);
        var player = await CreatePlayerViaHttpAsync(adminClient, ValidCreatePlayerInput($"Close {Guid.CreateVersion7():N}"), cancellationToken);
        var assignmentId = await GetSingleAssignmentIdAsync(created.CampaignId, player.PlayerId, cancellationToken);

        var blocked = await GetCloseoutReadinessAsync(adminClient, created.CampaignId, cancellationToken);
        blocked.IsReady.ShouldBeFalse();
        blocked.Status.ShouldBe(CampaignStatus.Active);
        blocked.Summary.UndecidedCount.ShouldBe(1);
        blocked.Blockers.ShouldContain(blocker => blocker.Condition == CloseoutBlockerConditions.Outcomes);

        var placementToken = (await GetPlacementRosterAsync(adminClient, created.CampaignId, cancellationToken))
            .Items.ShouldHaveSingleItem().ConcurrencyToken;
        using (var placeResponse = await adminClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.NotSelected, teamId: null, placementToken),
            cancellationToken))
        {
            placeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var ready = await GetCloseoutReadinessAsync(adminClient, created.CampaignId, cancellationToken);
        ready.IsReady.ShouldBeTrue();
        ready.Blockers.ShouldBeEmpty();

        using (var closeResponse = await adminClient.PostAsync(CampaignEndpoints.CloseUrl(created.CampaignId), content: null, cancellationToken))
        {
            closeResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await using (var context = fixture.CreateAdminContext())
        {
            var campaign = await context.Campaigns
                .SingleAsync(candidate => candidate.CampaignId == created.CampaignId, cancellationToken);
            campaign.Status.ShouldBe(CampaignStatus.Closed);
            campaign.ClosedAt.ShouldNotBeNull();
            campaign.ClosedById.ShouldBe(admin.UserId);

            var events = await context.ActivityEvents
                .Where(candidate => candidate.CampaignId == created.CampaignId)
                .OrderBy(candidate => candidate.ActivityEventId)
                .ToListAsync(cancellationToken);
            events.Count.ShouldBe(2);
            events.ShouldAllBe(activityEvent => activityEvent.ClubId == admin.Club.ClubId);
            events[0].EventKind.ShouldBe(ActivityEventKind.PlacementNotSelected);
            events[1].EventKind.ShouldBe(ActivityEventKind.CampaignClosed);
        }

        using (var detailResponse = await evaluatorClient.GetAsync(CampaignEndpoints.GetCampaignDetailUrl(created.CampaignId), cancellationToken))
        {
            detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var participantResponse = await evaluatorClient.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(created.CampaignId, assignmentId),
            cancellationToken))
        {
            participantResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var noteResponse = await evaluatorClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            new AddEvaluationNoteInput { PlayerCampaignAssignmentId = assignmentId, Content = "Late note." },
            cancellationToken))
        {
            noteResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        var closedToken = (await GetPlacementRosterAsync(adminClient, created.CampaignId, cancellationToken))
            .Items.ShouldHaveSingleItem().ConcurrencyToken;
        using (var placementResponse = await adminClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.NotSelected, teamId: null, closedToken),
            cancellationToken))
        {
            placementResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
    }

    /// <summary>
    /// Reopening a closed campaign restores Active status and writability (the evaluator can add a
    /// note again) while preserving the previously decided placement outcome and the ordered
    /// Close-then-Reopen activity feed.
    /// </summary>
    [Fact]
    public async Task ReopenJourney_RestoresWritability_PreservesOutcomes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "journey-reopen", cancellationToken);
        var evaluatorClient = await RegisterClubEvaluatorAsync("journey-reopen", admin.Club.ClubId, "Reese", "Evaluator", cancellationToken);

        var created = await CreateCampaignViaHttpAsync(adminClient, cancellationToken);
        var player = await CreatePlayerViaHttpAsync(adminClient, ValidCreatePlayerInput($"Reopen {Guid.CreateVersion7():N}"), cancellationToken);
        var assignmentId = await GetSingleAssignmentIdAsync(created.CampaignId, player.PlayerId, cancellationToken);

        var placementToken = (await GetPlacementRosterAsync(adminClient, created.CampaignId, cancellationToken))
            .Items.ShouldHaveSingleItem().ConcurrencyToken;
        using (var placeResponse = await adminClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.NotSelected, teamId: null, placementToken),
            cancellationToken))
        {
            placeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var closeResponse = await adminClient.PostAsync(CampaignEndpoints.CloseUrl(created.CampaignId), content: null, cancellationToken))
        {
            closeResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var reopenResponse = await adminClient.PostAsync(CampaignEndpoints.ReopenUrl(created.CampaignId), content: null, cancellationToken))
        {
            reopenResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await using (var context = fixture.CreateAdminContext())
        {
            var campaign = await context.Campaigns
                .SingleAsync(candidate => candidate.CampaignId == created.CampaignId, cancellationToken);
            campaign.Status.ShouldBe(CampaignStatus.Active);
            campaign.ClosedAt.ShouldBeNull();
            campaign.ClosedById.ShouldBeNull();

            var events = await context.ActivityEvents
                .Where(candidate => candidate.CampaignId == created.CampaignId)
                .OrderBy(candidate => candidate.ActivityEventId)
                .Select(candidate => candidate.EventKind)
                .ToListAsync(cancellationToken);
            events.ShouldBe(
            [
                ActivityEventKind.PlacementNotSelected,
                ActivityEventKind.CampaignClosed,
                ActivityEventKind.CampaignReopened
            ]);

            var assignment = await context.PlayerCampaignAssignments
                .SingleAsync(candidate => candidate.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
            assignment.PlacementOutcome.ShouldBe(PlacementOutcome.NotSelected);
        }

        using (var noteResponse = await evaluatorClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            new AddEvaluationNoteInput { PlayerCampaignAssignmentId = assignmentId, Content = "Reopened note." },
            cancellationToken))
        {
            noteResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var activityResponse = await adminClient.GetAsync(
            CampaignEndpoints.GetCampaignActivityUrl(new GetCampaignActivityInput { CampaignId = created.CampaignId }),
            cancellationToken);
        activityResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var activity = await activityResponse.Content.ReadFromJsonAsync<CampaignActivityResult>(cancellationToken);
        activity.ShouldNotBeNull();
        activity.Events.Select(item => item.EventType)
            .ShouldBe([CampaignLifecycleEventType.Reopened, CampaignLifecycleEventType.Closed]);
    }

    /// <summary>
    /// Two concurrent player creates with the identical payload while an Active campaign exists both
    /// succeed as distinct players, each with exactly one durable participation row. There is no
    /// HTTP-level idempotency key for player creation (the operation id is generated server-side per
    /// request), so concurrent duplicate creates do not collapse into a single row and do not
    /// double-enroll.
    /// </summary>
    [Fact]
    public async Task LateEnrollment_ConcurrentCreates_BothPersistWithSingleDurableRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var admin = await RegisterClubAdminAsync(adminClient, "journey-concurrent", cancellationToken);

        var created = await CreateCampaignViaHttpAsync(adminClient, cancellationToken);
        created.EnrolledPlayerCount.ShouldBe(0);

        var input = ValidCreatePlayerInput($"Concurrent {Guid.CreateVersion7():N}");

        var task1 = adminClient.PostAsJsonAsync(PlayerEndpoints.Create, input, cancellationToken);
        var task2 = adminClient.PostAsJsonAsync(PlayerEndpoints.Create, input, cancellationToken);

        using var response1 = await task1;
        using var response2 = await task2;

        response1.StatusCode.ShouldBe(HttpStatusCode.Created);
        response2.StatusCode.ShouldBe(HttpStatusCode.Created);

        var player1 = await response1.Content.ReadFromJsonAsync<PlayerDto>(cancellationToken);
        var player2 = await response2.Content.ReadFromJsonAsync<PlayerDto>(cancellationToken);
        player1.ShouldNotBeNull();
        player2.ShouldNotBeNull();
        player1.PlayerId.ShouldNotBe(player2.PlayerId);

        await using var context = fixture.CreateAdminContext();
        var persistedPlayerIds = await context.Players
            .Where(player => player.ClubId == admin.Club.ClubId
                && player.FirstName == input.FirstName
                && player.LastName == input.LastName)
            .Select(player => player.PlayerId)
            .ToListAsync(cancellationToken);
        persistedPlayerIds.Count.ShouldBe(2);
        persistedPlayerIds.ShouldContain(player1.PlayerId);
        persistedPlayerIds.ShouldContain(player2.PlayerId);

        foreach (var playerId in new[] { player1.PlayerId, player2.PlayerId })
        {
            var assignments = await context.PlayerCampaignAssignments
                .Where(assignment => assignment.PlayerId == playerId && assignment.CampaignId == created.CampaignId)
                .ToListAsync(cancellationToken);
            assignments.Count.ShouldBe(1, "each concurrently created player should have exactly one participation row");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Registers an admin, creates their club over HTTP, and refreshes the membership cookie.</summary>
    private async Task<ClubAdmin> RegisterClubAdminAsync(HttpClient client, string prefix, CancellationToken cancellationToken)
    {
        var email = SeedingHelpers.UniqueEmail($"{prefix}-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, clubId: null, cancellationToken, firstName: "Admin", lastName: "Creator");
        var club = await SeedingHelpers.CreateClubAsync(client, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
        var userId = await GetUserIdByEmailAsync(email, cancellationToken);
        return new ClubAdmin(userId, club);
    }

    /// <summary>Registers a non-admin club member over HTTP and assigns them to the given club.</summary>
    private async Task<HttpClient> RegisterClubEvaluatorAsync(string prefix, long clubId, string firstName, string lastName, CancellationToken cancellationToken)
    {
        var client = fixture.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail($"{prefix}-evaluator");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, clubId, cancellationToken, firstName: firstName, lastName: lastName);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
        return client;
    }

    private static async Task<CreateCampaignResult> CreateCampaignViaHttpAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var input = new CreateCampaignInput
        {
            OperationId = Guid.CreateVersion7(),
            Name = $"Journey Campaign {Guid.CreateVersion7():N}",
            StartDate = new DateOnly(2026, 6, 1),
            PlannedEndDate = new DateOnly(2026, 6, 30),
            InlineSeason = new InlineSeasonInput
            {
                Name = $"Journey Season {Guid.CreateVersion7():N}",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 12, 31)
            }
        };

        using var response = await client.PostAsJsonAsync(CampaignEndpoints.Create, input, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<CreateCampaignResult>(cancellationToken);
        result.ShouldNotBeNull();
        return result;
    }

    private static async Task<PlayerDto> CreatePlayerViaHttpAsync(HttpClient client, CreatePlayerInput input, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(PlayerEndpoints.Create, input, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var player = await response.Content.ReadFromJsonAsync<PlayerDto>(cancellationToken);
        player.ShouldNotBeNull();
        return player;
    }

    private static async Task<TeamDto> CreateTeamViaHttpAsync(HttpClient client, int graduationYear, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            TeamEndpoints.Create,
            new CreateTeamInput { Name = $"Journey Team {Guid.CreateVersion7():N}", GraduationYear = graduationYear },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var team = await response.Content.ReadFromJsonAsync<TeamDto>(cancellationToken);
        team.ShouldNotBeNull();
        return team;
    }

    private static CreatePlayerInput ValidCreatePlayerInput(string firstName) => new()
    {
        FirstName = firstName,
        LastName = "Player",
        DateOfBirth = new DateOnly(2012, 6, 15),
        GraduationYear = 2030
    };

    private static async Task<PagedResult<CampaignParticipantRosterItem>> GetParticipantRosterAsync(
        HttpClient client,
        long campaignId,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = campaignId }),
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await response.Content.ReadFromJsonAsync<PagedResult<CampaignParticipantRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        return roster;
    }

    private static async Task<CampaignParticipantDetailDto> GetParticipantDetailAsync(
        HttpClient client,
        long campaignId,
        long assignmentId,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(cancellationToken);
        detail.ShouldNotBeNull();
        return detail;
    }

    private static async Task<PagedResult<CampaignPlacementRosterItem>> GetPlacementRosterAsync(
        HttpClient client,
        long campaignId,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput { CampaignId = campaignId }),
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await response.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        return roster;
    }

    private static async Task<CampaignPlacementSummaryDto> GetPlacementSummaryAsync(
        HttpClient client,
        long campaignId,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(campaignId),
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<CampaignPlacementSummaryDto>(cancellationToken);
        summary.ShouldNotBeNull();
        return summary;
    }

    private static async Task<CampaignCloseoutReadinessDto> GetCloseoutReadinessAsync(
        HttpClient client,
        long campaignId,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignCloseoutReadinessUrl(campaignId),
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var readiness = await response.Content.ReadFromJsonAsync<CampaignCloseoutReadinessDto>(cancellationToken);
        readiness.ShouldNotBeNull();
        return readiness;
    }

    private async Task<long> GetUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        return await context.Users
            .Where(candidate => candidate.NormalizedEmail == email.ToUpperInvariant())
            .Select(candidate => candidate.Id)
            .SingleAsync(cancellationToken);
    }

    private async Task<long> GetSingleAssignmentIdAsync(long campaignId, long playerId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        return await context.PlayerCampaignAssignments
            .Where(assignment => assignment.CampaignId == campaignId && assignment.PlayerId == playerId)
            .Select(assignment => assignment.PlayerCampaignAssignmentId)
            .SingleAsync(cancellationToken);
    }

    private sealed record ClubAdmin(long UserId, ClubDto Club);
}
