using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Clubs;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the player roster query endpoint and filter behavior.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class PlayerRosterHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies that a same-club member can read the active roster and projected summary fields.
    /// </summary>
    [Fact]
    public async Task GetPlayerRoster_ReturnsActiveRows_ForSameClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var evaluatorClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("roster-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Alex", "RosterAdmin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, "Roster Club", "Austin", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var evaluatorEmail = UniqueEmail("roster-evaluator");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(evaluatorClient, evaluatorEmail, Password, cancellationToken);
        await UpdateUserAsync(evaluatorEmail, "Taylor", "Evaluator", club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(evaluatorClient, cancellationToken);

        await SeedRosterAsync(club.ClubId, cancellationToken);

        var url = GetPlayerRosterEndpoints.GetRosterUrl(club.ClubId, search: null, lifecycleStatus: "active");
        using var response = await evaluatorClient.GetAsync(url, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<PagedResult<PlayerListItem>>(cancellationToken);
        payload.ShouldNotBeNull();
        payload.TotalCount.ShouldBe(1);
        payload.Items[0].DisplayName.ShouldBe("Avery Active");
        payload.Items[0].GraduationYear.ShouldBe(2032);
        payload.Items[0].ActiveCampaigns.ShouldContain("Summer Tryouts");
        payload.Items[0].CurrentTags.Select(tag => tag.Name).ShouldContain("Defender");
    }

    /// <summary>
    /// Verifies archived view and tag filtering over active-campaign tags.
    /// </summary>
    [Fact]
    public async Task GetPlayerRoster_AppliesArchivedAndTagFilters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("roster-filters-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Morgan", "Admin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, "Roster Filter Club", "Dallas", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        await SeedRosterAsync(club.ClubId, cancellationToken);

        using (var archivedResponse = await adminClient.GetAsync(
                   GetPlayerRosterEndpoints.GetRosterUrl(club.ClubId, lifecycleStatus: "archived"),
                   cancellationToken))
        {
            archivedResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var archivedPayload = await archivedResponse.Content.ReadFromJsonAsync<PagedResult<PlayerListItem>>(cancellationToken);
            archivedPayload.ShouldNotBeNull();
            archivedPayload.TotalCount.ShouldBe(1);
            archivedPayload.Items[0].DisplayName.ShouldBe("Riley Archived");
            archivedPayload.Items[0].LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        }

        await using var db = fixture.CreateAdminContext();
        var defenderTagId = await db.PlayerTags
            .Where(tag => tag.ClubId == club.ClubId && tag.Name == "Defender")
            .Select(tag => tag.PlayerTagId)
            .SingleAsync(cancellationToken);

        using var tagFilteredResponse = await adminClient.GetAsync(
            GetPlayerRosterEndpoints.GetRosterUrl(club.ClubId, playerTagId: defenderTagId),
            cancellationToken);
        tagFilteredResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tagFilteredPayload = await tagFilteredResponse.Content.ReadFromJsonAsync<PagedResult<PlayerListItem>>(cancellationToken);
        tagFilteredPayload.ShouldNotBeNull();
        tagFilteredPayload.TotalCount.ShouldBe(1);
        tagFilteredPayload.Items[0].DisplayName.ShouldBe("Avery Active");
    }

    /// <summary>
    /// Verifies the administrator workflow can create, update, archive, and restore while evaluators remain read-only.
    /// </summary>
    [Fact]
    public async Task PlayerRosterWorkflow_AdminRoundTrip_AndEvaluatorReadOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var evaluatorClient = fixture.CreateNovaHttpClient();

        var adminEmail = UniqueEmail("workflow-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, "Jordan", "WorkflowAdmin", clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, "Workflow Club", "Plano", "TX", cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var evaluatorEmail = UniqueEmail("workflow-evaluator");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(evaluatorClient, evaluatorEmail, Password, cancellationToken);
        await UpdateUserAsync(evaluatorEmail, "Quinn", "WorkflowEvaluator", club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(evaluatorClient, cancellationToken);

        var createInput = new CreatePlayerInput
        {
            FirstName = "Skyler",
            LastName = "Rivera",
            DateOfBirth = new DateOnly(2012, 9, 7),
            GraduationYear = 2031
        };

        using var evaluatorCreate = await evaluatorClient.PostAsJsonAsync(PlayerEndpoints.Create, createInput, cancellationToken);
        evaluatorCreate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var createResponse = await adminClient.PostAsJsonAsync(PlayerEndpoints.Create, createInput, cancellationToken);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await createResponse.Content.ReadFromJsonAsync<PlayerDto>(cancellationToken);
        created.ShouldNotBeNull();

        var updateInput = new UpdatePlayerInput
        {
            PlayerId = created.PlayerId,
            FirstName = "Skyler",
            LastName = "Updated",
            DateOfBirth = createInput.DateOfBirth,
            GraduationYear = 2032
        };

        using var updateResponse = await adminClient.PutAsJsonAsync(PlayerEndpoints.UpdateUrl(created.PlayerId), updateInput, cancellationToken);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var archiveResponse = await adminClient.PostAsync(PlayerEndpoints.ArchiveUrl(created.PlayerId), content: null, cancellationToken);
        archiveResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var evaluatorRestore = await evaluatorClient.PostAsync(PlayerEndpoints.RestoreUrl(created.PlayerId), content: null, cancellationToken);
        evaluatorRestore.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using (var archivedRoster = await adminClient.GetAsync(
                   GetPlayerRosterEndpoints.GetRosterUrl(club.ClubId, lifecycleStatus: "archived", search: "Updated"),
                   cancellationToken))
        {
            archivedRoster.StatusCode.ShouldBe(HttpStatusCode.OK);
            var archivedPayload = await archivedRoster.Content.ReadFromJsonAsync<PagedResult<PlayerListItem>>(cancellationToken);
            archivedPayload.ShouldNotBeNull();
            archivedPayload.TotalCount.ShouldBeGreaterThanOrEqualTo(1);
            archivedPayload.Items.ShouldContain(player => player.PlayerId == created.PlayerId && player.DisplayName == "Skyler Updated");
        }

        using var restoreResponse = await adminClient.PostAsync(PlayerEndpoints.RestoreUrl(created.PlayerId), content: null, cancellationToken);
        restoreResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var activeRoster = await adminClient.GetAsync(
            GetPlayerRosterEndpoints.GetRosterUrl(club.ClubId, lifecycleStatus: "active", search: "Updated"),
            cancellationToken);
        activeRoster.StatusCode.ShouldBe(HttpStatusCode.OK);
        var activePayload = await activeRoster.Content.ReadFromJsonAsync<PagedResult<PlayerListItem>>(cancellationToken);
        activePayload.ShouldNotBeNull();
        activePayload.TotalCount.ShouldBeGreaterThanOrEqualTo(1);
        activePayload.Items.ShouldContain(player => player.PlayerId == created.PlayerId && player.DisplayName == "Skyler Updated");
    }

    private async Task SeedRosterAsync(long clubId, CancellationToken cancellationToken)
    {
        await using var db = fixture.CreateAdminContext();
        var actorUserId = await db.Users
            .Where(user => user.ClubId == clubId)
            .Select(user => user.Id)
            .FirstAsync(cancellationToken);

        var season = new SeasonEntity
        {
            Name = $"Season-{Guid.CreateVersion7():N}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = clubId,
            CreatedById = actorUserId
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(cancellationToken);

        var campaign = new CampaignEntity
        {
            Name = "Summer Tryouts",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        db.Campaigns.Add(campaign);

        var defenderTag = new PlayerTagEntity
        {
            Name = "Defender",
            Color = "#0055AA",
            ClubId = clubId,
            CreatedById = actorUserId
        };
        db.PlayerTags.Add(defenderTag);
        await db.SaveChangesAsync(cancellationToken);

        var activePlayer = new PlayerEntity
        {
            FirstName = "Avery",
            LastName = "Active",
            DateOfBirth = new DateOnly(2012, 4, 1),
            GraduationYear = 2032,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        var archivedPlayer = new PlayerEntity
        {
            FirstName = "Riley",
            LastName = "Archived",
            DateOfBirth = new DateOnly(2011, 2, 2),
            GraduationYear = 2031,
            LifecycleStatus = LifecycleStatus.Archived,
            ArchivedAt = DateTimeOffset.UtcNow.AddDays(-10),
            ArchivedById = actorUserId,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        db.Players.AddRange(activePlayer, archivedPlayer);
        await db.SaveChangesAsync(cancellationToken);

        var activeAssignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = activePlayer.PlayerId,
            CampaignId = campaign.CampaignId,
            TryoutNumber = 12,
            PlacementOutcome = PlacementOutcome.Undecided,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        db.PlayerCampaignAssignments.Add(activeAssignment);
        await db.SaveChangesAsync(cancellationToken);

        db.CampaignTagApplications.Add(new CampaignTagApplicationEntity
        {
            PlayerCampaignAssignmentId = activeAssignment.PlayerCampaignAssignmentId,
            PlayerTagId = defenderTag.PlayerTagId,
            ClubId = clubId,
            CreatedById = actorUserId
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ClubDto> CreateClubAsync(
        HttpClient client,
        string name,
        string city,
        string state,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(ClubEndpoints.Create, new CreateClubInput
        {
            Name = name,
            City = city,
            State = state
        }, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();
        return club;
    }

    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    private async Task UpdateUserAsync(
        string email,
        string firstName,
        string lastName,
        long? clubId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var normalizedEmail = email.ToUpperInvariant();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.ClubId = clubId;
        context.Users.Update(user);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";
}
