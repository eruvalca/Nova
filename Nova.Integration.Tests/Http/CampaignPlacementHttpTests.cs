using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Account;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the campaign placement update, roster, and summary endpoints.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignPlacementHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>Verifies same-season prior withdrawal requires administrator authority while other decisions permit members.</summary>
    /// <param name="priorOutcome">The latest decision in the earlier campaign.</param>
    /// <param name="isAdmin">Whether the caller administers the club.</param>
    /// <param name="expectedStatus">The expected endpoint status.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Withdrawn, false, HttpStatusCode.Forbidden)]
    [InlineData(PlacementOutcome.Withdrawn, true, HttpStatusCode.OK)]
    [InlineData(PlacementOutcome.Assigned, false, HttpStatusCode.OK)]
    [InlineData(PlacementOutcome.NotSelected, false, HttpStatusCode.OK)]
    public async Task CampaignPlacementUpdate_EnforcesPriorWithdrawalAuthority_AndPreservesHistory(
        PlacementOutcome priorOutcome, bool isAdmin, HttpStatusCode expectedStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("placement-supersession-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, adminEmail, cancellationToken);
        var sourceId = await SeedPriorDecisionAsync(assignmentId, teamId, priorOutcome, cancellationToken);
        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("placement-supersession-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);
        await using var before = fixture.CreateAdminContext();
        var original = await before.PlayerCampaignAssignments.AsNoTracking().SingleAsync(row => row.PlayerCampaignAssignmentId == sourceId, cancellationToken);
        var eventCount = await before.ActivityEvents.CountAsync(row => row.ClubId == club.ClubId, cancellationToken);

        using var response = await (isAdmin ? adminClient : memberClient).PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token), cancellationToken);
        response.StatusCode.ShouldBe(expectedStatus);
        await using var verify = fixture.CreateAdminContext();
        var source = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == sourceId, cancellationToken);
        source.PlacementOutcome.ShouldBe(original.PlacementOutcome);
        source.TeamId.ShouldBe(original.TeamId);
        source.ConcurrencyToken.ShouldBe(original.ConcurrencyToken);
        source.DecisionRecordedById.ShouldBe(original.DecisionRecordedById);
        source.DecisionRecordedAt.ShouldBe(original.DecisionRecordedAt);
        var target = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        if (expectedStatus == HttpStatusCode.OK)
        {
            var success = await response.Content.ReadFromJsonAsync<PlacementMutationSuccess>(cancellationToken);
            target.ConcurrencyToken.ShouldBe(success.ConcurrencyToken);
            target.ConcurrencyToken.ShouldNotBe(token);
            target.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
            target.DecisionRecordedAt.ShouldNotBeNull();
            var lastEvent = await verify.ActivityEvents.Where(row => row.ClubId == club.ClubId)
                .OrderByDescending(row => row.ActivityEventId).FirstAsync(cancellationToken);
            lastEvent.EventKind.ShouldBe(ActivityEventKind.PlacementSuperseded);
            (await verify.ActivityEvents.CountAsync(row => row.ClubId == club.ClubId, cancellationToken)).ShouldBe(eventCount + 1);
        }
        else
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
            problem.ShouldNotBeNull();
            problem.Status.ShouldBe((int)HttpStatusCode.Forbidden);
            problem.Extensions.ShouldContainKey("traceId");
            target.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
            target.ConcurrencyToken.ShouldBe(token);
            (await verify.ActivityEvents.CountAsync(row => row.ClubId == club.ClubId, cancellationToken)).ShouldBe(eventCount);
        }
    }

    /// <summary>Verifies owning withdrawal cannot be replaced and enrollment cannot be submitted as a saved decision.</summary>
    /// <param name="initialOutcome">The campaign-local state.</param>
    /// <param name="requestedOutcome">The requested replacement.</param>
    /// <param name="expectedStatus">The expected endpoint status.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Withdrawn, PlacementOutcome.NotSelected, HttpStatusCode.Conflict)]
    [InlineData(PlacementOutcome.Undecided, PlacementOutcome.Undecided, HttpStatusCode.BadRequest)]
    public async Task CampaignPlacementUpdate_RejectsTerminalWithdrawalAndEnrollmentInput(
        PlacementOutcome initialOutcome, PlacementOutcome requestedOutcome, HttpStatusCode expectedStatus)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-terminal");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, _, token) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken, initialOutcome: initialOutcome);

        using var response = await client.PutAsJsonAsync(CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, requestedOutcome, null, token), cancellationToken);
        response.StatusCode.ShouldBe(expectedStatus);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe((int)expectedStatus);
        problem.Extensions.ShouldContainKey("traceId");
        await using var verify = fixture.CreateAdminContext();
        var target = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        target.PlacementOutcome.ShouldBe(initialOutcome);
        target.ConcurrencyToken.ShouldBe(token);
        (await verify.PlacementMutationReceipts.CountAsync(row => row.ClubId == club.ClubId, cancellationToken)).ShouldBe(0);
    }

    /// <summary>Seeds one earlier Closed decision with explicitly ordered opening metadata.</summary>
    /// <param name="targetId">The current participation identifier.</param>
    /// <param name="teamId">The team to use for a saved assignment.</param>
    /// <param name="outcome">The saved earlier outcome.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The earlier participation identifier.</returns>
    private async Task<long> SeedPriorDecisionAsync(long targetId, long teamId, PlacementOutcome outcome, CancellationToken cancellationToken)
    {
        await using var db = fixture.CreateAdminContext();
        var target = await db.PlayerCampaignAssignments.Include(row => row.Campaign)
            .SingleAsync(row => row.PlayerCampaignAssignmentId == targetId, cancellationToken);
        target.Campaign.SeasonOpeningSequence = 2;
        await db.SaveChangesAsync(cancellationToken);
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Earlier placement {Guid.NewGuid():N}",
            StartDate = new DateOnly(2026, 5, 1),
            Status = CampaignStatus.Closed,
            SeasonId = target.Campaign.SeasonId,
            ClubId = target.ClubId,
            CreatedById = target.CreatedById,
            SeasonOpeningSequence = 1,
            ClosedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ClosedById = target.CreatedById
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(cancellationToken);
        var prior = new PlayerCampaignAssignmentEntity
        {
            CampaignId = campaign.CampaignId,
            PlayerId = target.PlayerId,
            ClubId = target.ClubId,
            CreatedById = target.CreatedById,
            PlacementOutcome = outcome,
            TeamId = outcome == PlacementOutcome.Assigned ? teamId : null,
            DecisionRecordedAt = DateTimeOffset.UtcNow.AddDays(-2),
            DecisionRecordedById = target.CreatedById,
            DecisionActorDisplayName = "Earlier recorder",
            ConcurrencyToken = Guid.NewGuid()
        };
        db.PlayerCampaignAssignments.Add(prior);
        await db.SaveChangesAsync(cancellationToken);
        return prior.PlayerCampaignAssignmentId;
    }

    /// <summary>
    /// Verifies anonymous callers receive an unauthorized response for placement updates.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsUnauthorized_ForAnonymousCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using var response = await anonymousClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(1),
            new UpdateCampaignPlacementInput(1, PlacementOutcome.Assigned, 2, Guid.NewGuid()),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies an approved club member can update a placement and receives a replacement concurrency token.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsOk_WithReplacementToken_AndPersistsPlacement_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("placement-member-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, adminEmail, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("placement-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        using var response = await memberClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var success = await response.Content.ReadFromJsonAsync<PlacementMutationSuccess>(cancellationToken);
        success.ConcurrencyToken.ShouldNotBe(Guid.Empty);
        success.ConcurrencyToken.ShouldNotBe(token);

        await using var context = fixture.CreateAdminContext();
        var persisted = await context.PlayerCampaignAssignments
            .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        persisted.TeamId.ShouldBe(teamId);
        persisted.ConcurrencyToken.ShouldBe(success.ConcurrencyToken);
    }

    /// <summary>
    /// Verifies an authenticated user without a club receives a forbidden response for placement updates.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsForbidden_ForAuthenticatedUserWithoutClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("placement-no-club-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, adminEmail, cancellationToken);

        using var noClubClient = fixture.CreateNovaHttpClient();
        var noClubEmail = UniqueEmail("placement-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(noClubClient, noClubEmail, Password, cancellationToken);
        await UpdateUserAsync(noClubEmail, clubId: null, cancellationToken);
        await RefreshClubMembershipCookieAsync(noClubClient, cancellationToken);

        using var response = await noClubClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a club administrator can update a placement and receives a replacement concurrency token.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsOk_WithReplacementToken_AndPersistsPlacement_ForClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-success");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var success = await response.Content.ReadFromJsonAsync<PlacementMutationSuccess>(cancellationToken);
        success.ConcurrencyToken.ShouldNotBe(Guid.Empty);
        success.ConcurrencyToken.ShouldNotBe(token);

        await using var context = fixture.CreateAdminContext();
        var persisted = await context.PlayerCampaignAssignments
            .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        persisted.TeamId.ShouldBe(teamId);
        persisted.ConcurrencyToken.ShouldBe(success.ConcurrencyToken);
    }

    /// <summary>
    /// Verifies a route/body identifier mismatch is rejected with a bad-request problem.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsBadRequest_WhenRouteAndBodyAssignmentIdsDiffer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-mismatch");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId + 1, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("The player campaign assignment identifier in the route does not match the request body.");
    }

    /// <summary>
    /// Verifies an Assigned outcome without a team is rejected by endpoint validation.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsValidationProblem_WhenAssignedOutcomeLacksTeam()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-no-team");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, _, token) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId: null, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await ReadErrorsAsync(response, cancellationToken);
        errors.ShouldContainKey(nameof(UpdateCampaignPlacementInput.TeamId));
    }

    /// <summary>
    /// Verifies an unparseable JSON payload is rejected before the handler runs. The framework's
    /// body binding throws BadHttpRequestException, which the API exception-handler pipeline maps
    /// to a 400 ProblemDetails response.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsBadRequest_ForUnparseableJsonBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-malformed");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, _, _) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.PutAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new StringContent("{ not json", Encoding.UTF8, "application/json"),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("title").GetString().ShouldBe("Bad Request");
        document.RootElement.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies another club's participation is hidden behind a not-found response.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsNotFound_ForCrossTenantAssignment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var ownerClient = fixture.CreateNovaHttpClient();
        var ownerEmail = UniqueEmail("placement-owner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(ownerClient, ownerEmail, Password, cancellationToken);
        await UpdateUserAsync(ownerEmail, clubId: null, cancellationToken);
        var ownerClub = await CreateClubAsync(ownerClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(ownerClient, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(ownerClub.ClubId, ownerEmail, cancellationToken);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("placement-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        otherClub.ClubId.ShouldNotBe(ownerClub.ClubId);

        using var response = await otherClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Verifies a team identifier from another club is hidden behind a non-disclosing not-found.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsNotFound_ForCrossTenantTeam()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ownerClient = fixture.CreateNovaHttpClient();
        var ownerEmail = UniqueEmail("placement-cross-team-owner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(ownerClient, ownerEmail, Password, cancellationToken);
        await UpdateUserAsync(ownerEmail, clubId: null, cancellationToken);
        var ownerClub = await CreateClubAsync(ownerClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(ownerClient, cancellationToken);
        var (assignmentId, _, token) = await SeedPlacementDataAsync(ownerClub.ClubId, ownerEmail, cancellationToken);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("placement-cross-team-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        var foreignTeamId = await SeedingHelpers.InsertTeamAsync(
            fixture,
            otherClub.ClubId,
            otherEmail,
            $"Foreign Team {Guid.NewGuid():N}",
            2028,
            cancellationToken);

        using var response = await ownerClient.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, foreignTeamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var problemDocument = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        foreach (var property in problemDocument.RootElement.EnumerateObject())
        {
            if (property.NameEquals("traceId"))
            {
                continue;
            }

            property.Value.ToString().ShouldNotBe(foreignTeamId.ToString());
            property.Value.ToString().ShouldNotBe(otherClub.ClubId.ToString());
        }
    }

    /// <summary>
    /// Verifies archived teams cannot receive new placement decisions.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsConflict_ForArchivedTeam()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-archived-team");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(
            club.ClubId,
            email,
            cancellationToken,
            archivedTeam: true);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        problem.ShouldNotBeNull();
        problem.Detail.ShouldBe("Archived teams cannot receive new placements.");
    }

    /// <summary>
    /// Verifies non-assigned outcomes clear a previously assigned team at the PostgreSQL boundary.
    /// </summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.NotSelected)]
    [InlineData(PlacementOutcome.Withdrawn)]
    public async Task CampaignPlacementUpdate_ClearsTeam_ForNonAssignedOutcomes(PlacementOutcome outcome)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-clears-team");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(
            club.ClubId,
            email,
            cancellationToken,
            initialOutcome: PlacementOutcome.Assigned);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, outcome, teamId: null, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var success = await response.Content.ReadFromJsonAsync<PlacementMutationSuccess>(cancellationToken);
        success.ConcurrencyToken.ShouldNotBe(token);

        await using var context = fixture.CreateAdminContext();
        var persisted = await context.PlayerCampaignAssignments
            .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        persisted.PlacementOutcome.ShouldBe(outcome);
        persisted.TeamId.ShouldBeNull();
        persisted.ConcurrencyToken.ShouldBe(success.ConcurrencyToken);
    }

    /// <summary>
    /// Verifies a stale concurrency token conflicts and never overwrites the winning update.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsConflict_AndPreservesWinner_WhenTokenIsStale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-stale");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(club.ClubId, email, cancellationToken);

        var newerToken = Guid.NewGuid();
        await using (var update = fixture.CreateAdminContext())
        {
            var participation = await update.PlayerCampaignAssignments
                .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
            participation.PlacementOutcome = PlacementOutcome.Withdrawn;
            participation.ConcurrencyToken = newerToken;
            await update.SaveChangesAsync(cancellationToken);
        }

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("The placement was changed by another user. Reload it and try again.");

        await using var context = fixture.CreateAdminContext();
        var persisted = await context.PlayerCampaignAssignments
            .SingleAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Withdrawn);
        persisted.TeamId.ShouldBeNull();
        persisted.ConcurrencyToken.ShouldBe(newerToken);
    }

    /// <summary>
    /// Verifies a closed campaign rejects placement mutations with a conflict.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsConflict_ForClosedCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-closed");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(
            club.ClubId, email, cancellationToken, campaignStatus: CampaignStatus.Closed);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.NotSelected, teamId: null, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Only active campaigns can accept placement changes.");
    }

    /// <summary>
    /// Verifies a Draft campaign rejects placement mutations without changing the assignment.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsConflict_AndDoesNotWrite_ForDraftCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-draft");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, _, token) = await SeedPlacementDataAsync(
            club.ClubId,
            email,
            cancellationToken,
            campaignStatus: CampaignStatus.Draft);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.NotSelected, teamId: null, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Only active campaigns can accept placement changes.");

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.PlayerCampaignAssignments
            .SingleAsync(
                assignment => assignment.PlayerCampaignAssignmentId == assignmentId,
                cancellationToken);
        persisted.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        persisted.TeamId.ShouldBeNull();
        persisted.ConcurrencyToken.ShouldBe(token);
        var activityCount = await verify.ActivityEvents
            .CountAsync(activity => activity.CampaignId == persisted.CampaignId, cancellationToken);
        activityCount.ShouldBe(0);
    }

    /// <summary>
    /// Verifies an archived player rejects placement mutations with a conflict.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsConflict_ForArchivedPlayer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-archived-player");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, _, token) = await SeedPlacementDataAsync(
            club.ClubId, email, cancellationToken, archivedPlayer: true);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.NotSelected, teamId: null, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Archived players cannot receive new placement decisions.");
    }

    /// <summary>
    /// Verifies an ineligible team is rejected with a validation problem naming the team field.
    /// </summary>
    [Fact]
    public async Task CampaignPlacementUpdate_ReturnsValidationProblem_ForIneligibleTeam()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-ineligible");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (assignmentId, teamId, token) = await SeedPlacementDataAsync(
            club.ClubId, email, cancellationToken, teamGraduationYear: 2031);

        using var response = await client.PutAsJsonAsync(
            CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId),
            new UpdateCampaignPlacementInput(assignmentId, PlacementOutcome.Assigned, teamId, token),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await ReadErrorsAsync(response, cancellationToken);
        errors.ShouldContainKey(nameof(UpdateCampaignPlacementInput.TeamId));
    }

    /// <summary>
    /// Verifies a current-club member receives a bounded, ordered placement page with the
    /// persisted fields needed for a placement update.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ReturnsOrderedPage_ForCurrentClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-roster");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                Page = 1,
                PageSize = 50
            }),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await response.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        roster.TotalCount.ShouldBe(5);
        roster.Items.Count.ShouldBe(5);

        roster.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe(
            [
                seeded.ZoeAdamsAssignedId,
                seeded.ZoeAdamsUndecidedId,
                seeded.AmyBrownId,
                seeded.CaraChenId,
                seeded.DrewDavisId
            ]);

        var assigned = roster.Items[0];
        assigned.PlayerId.ShouldBeGreaterThan(0);
        assigned.DisplayName.ShouldBe("Zoe Adams");
        assigned.GraduationYear.ShouldBe(2028);
        assigned.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        assigned.Team.ShouldNotBeNull();
        assigned.Team!.TeamId.ShouldBe(seeded.TeamId);
        assigned.ConcurrencyToken.ShouldNotBe(Guid.Empty);

        roster.Items.ShouldAllBe(item => item.ConcurrencyToken != Guid.Empty);
        roster.Items.ShouldAllBe(item => !string.IsNullOrWhiteSpace(item.DisplayName));
    }

    /// <summary>
    /// Verifies the graduation-year and unresolved-only filters compose and bound the page.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ComposesGraduationYearAndUnresolvedOnlyFilters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-roster-filters");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, email, cancellationToken);

        using var byYearResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                GraduationYear = 2028,
                Page = 1,
                PageSize = 50
            }),
            cancellationToken);
        byYearResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var byYear = await byYearResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        byYear.ShouldNotBeNull();
        byYear.TotalCount.ShouldBe(2);
        byYear.Items.ShouldAllBe(item => item.GraduationYear == 2028);

        using var unresolvedResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                UnresolvedOnly = true,
                Page = 1,
                PageSize = 50
            }),
            cancellationToken);
        unresolvedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var unresolved = await unresolvedResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        unresolved.ShouldNotBeNull();
        unresolved.TotalCount.ShouldBe(2);
        unresolved.Items.ShouldAllBe(item => item.PlacementOutcome == PlacementOutcome.Undecided);

        using var composedResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                GraduationYear = 2028,
                UnresolvedOnly = true,
                Page = 1,
                PageSize = 50
            }),
            cancellationToken);
        composedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var composed = await composedResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        composed.ShouldNotBeNull();
        composed.TotalCount.ShouldBe(1);
        composed.Items.Single().PlayerCampaignAssignmentId.ShouldBe(seeded.AmyBrownId);
    }

    /// <summary>
    /// Verifies paging slices the deterministic ordering with stable total counts.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_PagesDeterministicallyAcrossPages()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-roster-paging");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, email, cancellationToken);

        using var firstResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                Page = 1,
                PageSize = 2
            }),
            cancellationToken);
        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        first.ShouldNotBeNull();
        first.TotalCount.ShouldBe(5);
        first.Items.Count.ShouldBe(2);
        first.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe([seeded.ZoeAdamsAssignedId, seeded.ZoeAdamsUndecidedId]);

        using var secondResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                Page = 2,
                PageSize = 2
            }),
            cancellationToken);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = await secondResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        second.ShouldNotBeNull();
        second.TotalCount.ShouldBe(5);
        second.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe([seeded.AmyBrownId, seeded.CaraChenId]);

        using var thirdResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput
            {
                CampaignId = seeded.CampaignId,
                Page = 3,
                PageSize = 2
            }),
            cancellationToken);
        thirdResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var third = await thirdResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        third.ShouldNotBeNull();
        third.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe([seeded.DrewDavisId]);
    }

    /// <summary>
    /// Verifies the summary reports accurate whole-campaign counts independent of roster filters.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummary_ReturnsWholeCampaignCounts_IndependentOfRosterFilters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-summary");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(seeded.CampaignId),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<CampaignPlacementSummaryDto>(cancellationToken);
        summary.ShouldNotBeNull();
        summary.AssignedCount.ShouldBe(1);
        summary.NotSelectedCount.ShouldBe(1);
        summary.WithdrawnCount.ShouldBe(1);
        summary.UndecidedCount.ShouldBe(2);
        summary.TotalCount.ShouldBe(5);
    }

    /// <summary>
    /// Verifies a least-privileged club member without the ClubAdmin role can read both placement routes.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoutes_ReturnPayload_ForLeastPrivilegedClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // The club creator becomes a ClubAdmin, so a second user proves ordinary member access.
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("placement-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("placement-least-privileged");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, memberEmail, cancellationToken);

        using var rosterResponse = await memberClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput { CampaignId = seeded.CampaignId }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await rosterResponse.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        roster.TotalCount.ShouldBe(5);

        using var summaryResponse = await memberClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(seeded.CampaignId),
            cancellationToken);
        summaryResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await summaryResponse.Content.ReadFromJsonAsync<CampaignPlacementSummaryDto>(cancellationToken);
        summary.ShouldNotBeNull();
        summary.TotalCount.ShouldBe(5);
    }

    /// <summary>
    /// Verifies anonymous callers receive unauthorized responses for both placement routes.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoutes_ReturnUnauthorized_ForAnonymousCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using var rosterResponse = await anonymousClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput { CampaignId = 1 }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var summaryResponse = await anonymousClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(1),
            cancellationToken);
        summaryResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies authenticated callers without a club receive forbidden responses for both routes.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoutes_ReturnForbidden_ForAuthenticatedUserWithoutClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var rosterResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput { CampaignId = 1 }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var summaryResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(1),
            cancellationToken);
        summaryResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies cross-tenant campaign reads are rejected with non-disclosing not-found ProblemDetails.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoutes_ReturnNotFound_ForCrossTenantCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var currentClient = fixture.CreateNovaHttpClient();
        var currentEmail = UniqueEmail("placement-cross-tenant-current");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(currentClient, currentEmail, Password, cancellationToken);
        await UpdateUserAsync(currentEmail, clubId: null, cancellationToken);
        await CreateClubAsync(currentClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(currentClient, cancellationToken);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("placement-cross-tenant-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(otherClub.ClubId, otherEmail, cancellationToken);

        using var rosterResponse = await currentClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementRosterUrl(new GetCampaignPlacementRosterInput { CampaignId = seeded.CampaignId }),
            cancellationToken);
        rosterResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var rosterDocument = await JsonDocument.ParseAsync(
            await rosterResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        rosterDocument.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.NotFound);
        rosterDocument.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();

        using var summaryResponse = await currentClient.GetAsync(
            CampaignEndpoints.GetCampaignPlacementSummaryUrl(seeded.CampaignId),
            cancellationToken);
        summaryResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var summaryDocument = await JsonDocument.ParseAsync(
            await summaryResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        summaryDocument.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.NotFound);
        summaryDocument.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies invalid explicit query values are rejected with validation ProblemDetails before the handler runs.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ReturnsValidationProblem_ForInvalidExplicitQueryValues()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-invalid-query");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(
            "/api/campaigns/1/placements?graduationYear=0&pageSize=101",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies an overflowing page offset is rejected by endpoint input validation.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ReturnsValidationProblem_ForOverflowingPageOffset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-overflowing-page-offset");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(
            $"/api/campaigns/1/placements?page={int.MaxValue}&pageSize=2",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.BadRequest);
        document.RootElement.GetProperty("errors")
            .TryGetProperty(nameof(GetCampaignPlacementRosterInput.Page), out _)
            .ShouldBeTrue();
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies default paging applies when both query values are omitted at the endpoint boundary.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_AppliesDefaultPaging_WhenPageAndPageSizeAreOmitted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("placement-default-paging");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var seeded = await SeedPlacementQueryDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
            $"/api/campaigns/{seeded.CampaignId}/placements",
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await response.Content.ReadFromJsonAsync<PagedResult<CampaignPlacementRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        roster.Page.ShouldBe(GetCampaignPlacementRosterInput.DefaultPage);
        roster.PageSize.ShouldBe(GetCampaignPlacementRosterInput.DefaultPageSize);
        roster.TotalCount.ShouldBe(5);
    }

    /// <summary>
    /// Seeds a club, season, campaign, player, participation, and team through the admin context.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminEmail">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <param name="campaignStatus">The lifecycle status assigned to the campaign.</param>
    /// <param name="archivedPlayer">Whether the player should be seeded as archived.</param>
    /// <param name="teamGraduationYear">The team graduation-year cutoff.</param>
    /// <returns>The seeded assignment id, team id, and assignment concurrency token.</returns>
    private async Task<(long AssignmentId, long TeamId, Guid ConcurrencyToken)> SeedPlacementDataAsync(
        long clubId,
        string adminEmail,
        CancellationToken cancellationToken,
        CampaignStatus campaignStatus = CampaignStatus.Active,
        bool archivedPlayer = false,
        int teamGraduationYear = 2029,
        bool archivedTeam = false,
        PlacementOutcome initialOutcome = PlacementOutcome.Undecided)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(
            candidate => candidate.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Placement Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubId,
            CreatedById = user.Id
        };
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Placement Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = campaignStatus,
            ClosedAt = campaignStatus == CampaignStatus.Closed ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            ClosedById = campaignStatus == CampaignStatus.Closed ? user.Id : null,
            Season = season,
            SeasonId = 0,
            ClubId = clubId,
            CreatedById = user.Id
        };
        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Place",
            LastName = $"Player {suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = archivedPlayer ? LifecycleStatus.Archived : LifecycleStatus.Active,
            ArchivedAt = archivedPlayer ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            ArchivedById = archivedPlayer ? user.Id : null,
            ClubId = clubId,
            CreatedById = user.Id
        };
        var team = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Team {suffix}",
            GraduationYear = teamGraduationYear,
            LifecycleStatus = archivedTeam ? LifecycleStatus.Archived : LifecycleStatus.Active,
            ArchivedAt = archivedTeam ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            ArchivedById = archivedTeam ? user.Id : null,
            ClubId = clubId,
            CreatedById = user.Id
        };

        context.AddRange(season, campaign, player, team);
        await context.SaveChangesAsync(cancellationToken);
        var club = await context.Clubs.SingleAsync(row => row.ClubId == clubId, cancellationToken);
        club.CurrentSeasonId = season.SeasonId;
        await context.SaveChangesAsync(cancellationToken);

        var concurrencyToken = Guid.NewGuid();
        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = clubId,
            CreatedById = user.Id,
            PlacementOutcome = initialOutcome,
            DecisionRecordedAt = initialOutcome == PlacementOutcome.Undecided ? null : DateTimeOffset.UtcNow,
            DecisionRecordedById = initialOutcome == PlacementOutcome.Undecided ? null : user.Id,
            DecisionActorDisplayName = initialOutcome == PlacementOutcome.Undecided ? null : "Seed recorder",
            TeamId = initialOutcome == PlacementOutcome.Assigned ? team.TeamId : null,
            TryoutNumber = 7,
            ConcurrencyToken = concurrencyToken
        };
        context.Add(assignment);
        await context.SaveChangesAsync(cancellationToken);

        return (assignment.PlayerCampaignAssignmentId, team.TeamId, concurrencyToken);
    }

    /// <summary>
    /// Seeds a club campaign with five mixed-outcome participations and one team.
    /// </summary>
    /// <param name="clubId">The club identifier to seed into.</param>
    /// <param name="email">The seeding user's email, used to resolve the user identifier.</param>
    /// <param name="cancellationToken">A token to cancel the seeding.</param>
    /// <returns>The seeded campaign, team, and assignment identifiers.</returns>
    private async Task<PlacementSeedData> SeedPlacementQueryDataAsync(long clubId, string email, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);

        var season = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "Placement Season", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = user.Id };
        var campaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Placement Campaign", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = 0, ClubId = clubId, CreatedById = user.Id };
        var team = new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "Alpha", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };

        var zoeAdamsAssigned = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Zoe", LastName = "Adams", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };
        var zoeAdamsUndecided = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Zoe", LastName = "Adams", DateOfBirth = new DateOnly(2010, 2, 2), GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };
        var amyBrown = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Amy", LastName = "Brown", DateOfBirth = new DateOnly(2011, 3, 3), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };
        var caraChen = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Cara", LastName = "Chen", DateOfBirth = new DateOnly(2011, 4, 4), GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };
        var drewDavis = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Drew", LastName = "Davis", DateOfBirth = new DateOnly(2012, 5, 5), GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };

        context.AddRange(season, campaign, team, zoeAdamsAssigned, zoeAdamsUndecided, amyBrown, caraChen, drewDavis);
        await context.SaveChangesAsync(cancellationToken);

        var assignment1 = new PlayerCampaignAssignmentEntity { PlayerId = zoeAdamsAssigned.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.Assigned, TeamId = team.TeamId };
        var assignment2 = new PlayerCampaignAssignmentEntity { PlayerId = zoeAdamsUndecided.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.Undecided };
        var assignment3 = new PlayerCampaignAssignmentEntity { PlayerId = amyBrown.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.Undecided };
        var assignment4 = new PlayerCampaignAssignmentEntity { PlayerId = caraChen.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.NotSelected };
        var assignment5 = new PlayerCampaignAssignmentEntity { PlayerId = drewDavis.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.Withdrawn };

        context.AddRange(assignment1, assignment2, assignment3, assignment4, assignment5);
        await context.SaveChangesAsync(cancellationToken);

        return new PlacementSeedData(
            campaign.CampaignId,
            team.TeamId,
            assignment1.PlayerCampaignAssignmentId,
            assignment2.PlayerCampaignAssignmentId,
            assignment3.PlayerCampaignAssignmentId,
            assignment4.PlayerCampaignAssignmentId,
            assignment5.PlayerCampaignAssignmentId);
    }

    /// <summary>
    /// Identifiers produced by the placement seeding helper.
    /// </summary>
    private sealed record PlacementSeedData(
        long CampaignId,
        long TeamId,
        long ZoeAdamsAssignedId,
        long ZoeAdamsUndecidedId,
        long AmyBrownId,
        long CaraChenId,
        long DrewDavisId);

    /// <summary>
    /// Updates a user's club membership through the fixture admin context.
    /// </summary>
    /// <param name="email">The user's e-mail address.</param>
    /// <param name="clubId">The club identifier to assign, or <see langword="null"/> to clear membership.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
        => await SeedingHelpers.UpdateUserAsync(fixture, email, clubId, cancellationToken);

    /// <summary>
    /// Generates a unique e-mail address for a test user.
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
        using var response = await client.PostAsync(
            ClubEndpoints.Create,
            SeedingHelpers.CreateClubMultipartContent($"Club {Guid.NewGuid():N}", "X", "TX"),
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    /// <summary>
    /// Completes the club-membership flow so the client carries the refreshed membership cookie.
    /// </summary>
    /// <param name="client">The HTTP client whose membership cookie should be refreshed.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/dashboard", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>
    /// Reads the <c>errors</c> dictionary from a validation ProblemDetails payload.
    /// </summary>
    /// <param name="response">The problem-details response.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The validation error dictionary.</returns>
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
