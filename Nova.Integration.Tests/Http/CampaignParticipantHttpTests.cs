using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for campaign participant roster and detail endpoints.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignParticipantHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies the roster endpoint accepts repeated query values and returns the expected payload.
    /// </summary>
    [Fact]
    public async Task GetParticipantRoster_ReturnsOk_WithRepeatedFiltersAndTagAnnotations()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-roster");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (campaignId, tagId, _) = await SeedRosterDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput
            {
                CampaignId = campaignId,
                GraduationYears = [2028, 2029],
                TagDefinitionIds = [tagId],
                Page = 1,
                PageSize = 50
            }),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roster = await response.Content.ReadFromJsonAsync<PagedResult<CampaignParticipantRosterItem>>(cancellationToken);
        roster.ShouldNotBeNull();
        roster.TotalCount.ShouldBe(1);
        roster.Items.Count.ShouldBe(1);
        roster.Items[0].DisplayName.ShouldBe("Avery Adams");
        roster.Items[0].AppliedTags.ShouldContain(tag => tag.PlayerTagId == tagId);
    }

    /// <summary>
    /// Verifies the roster endpoint returns non-disclosing not-found ProblemDetails for missing campaigns.
    /// </summary>
    [Fact]
    public async Task GetParticipantRoster_ReturnsNotFoundProblem_ForMissingCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-roster-missing-campaign");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        await SeedRosterDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantRosterUrl(new GetCampaignParticipantRosterInput { CampaignId = 999_999, Page = 1, PageSize = 50 }),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.NotFound);
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Verifies the detail endpoint returns non-disclosing not-found ProblemDetails for missing participants.
    /// </summary>
    [Fact]
    public async Task GetParticipantDetail_ReturnsNotFoundProblem_ForMissingParticipant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("participant-detail");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (campaignId, _, assignmentId) = await SeedRosterDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId + 1),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("status").GetInt32().ShouldBe((int)HttpStatusCode.NotFound);
        document.RootElement.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    private static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            ClubEndpoints.Create,
            new CreateClubInput { Name = $"Club {Guid.NewGuid():N}", City = "X", State = "TX" },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        user.ClubId = clubId;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<(long CampaignId, long TagId, long AssignmentId)> SeedRosterDataAsync(long clubId, string email, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        var season = new SeasonEntity { Name = "Roster Season", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = user.Id };
        var campaign = new CampaignEntity { Name = "Roster Campaign", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, Season = season, SeasonId = 0, ClubId = clubId, CreatedById = user.Id };
        var player = new PlayerEntity { FirstName = "Avery", LastName = "Adams", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = clubId, CreatedById = user.Id };
        var playerTag = new PlayerTagEntity { Name = "Roster Tag", Color = "Blue", ClubId = clubId, CreatedById = user.Id, LifecycleStatus = LifecycleStatus.Active };

        context.AddRange(season, campaign, player, playerTag);
        await context.SaveChangesAsync(cancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity { PlayerId = player.PlayerId, CampaignId = campaign.CampaignId, ClubId = clubId, CreatedById = user.Id, PlacementOutcome = PlacementOutcome.Assigned, TryoutNumber = 7 };
        context.Add(assignment);
        await context.SaveChangesAsync(cancellationToken);

        context.CampaignTagApplications.Add(new CampaignTagApplicationEntity { PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId, PlayerTagId = playerTag.PlayerTagId, ClubId = clubId, CreatedById = user.Id });
        context.Notes.Add(new NoteEntity { PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId, ClubId = clubId, Content = "Roster note", CreatedById = user.Id });
        await context.SaveChangesAsync(cancellationToken);

        return (campaign.CampaignId, playerTag.PlayerTagId, assignment.PlayerCampaignAssignmentId);
    }
}
