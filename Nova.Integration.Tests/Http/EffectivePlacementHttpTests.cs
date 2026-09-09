using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Seasons;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>Proves every placement read route is registered, authorized, validated, and serialized.</summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class EffectivePlacementHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("current")]
    [InlineData("working")]
    [InlineData("closed")]
    public async Task PlacementReadRejectsAnonymousCallerAsync(string route)
    {
        using var client = fixture.CreateNovaHttpClient();
        using var response = await client.GetAsync(Route(route, 1), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("current")]
    [InlineData("working")]
    [InlineData("closed")]
    public async Task PlacementReadRejectsAuthenticatedCallerWithoutApprovedMembershipAsync(string route)
    {
        using var client = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client,
            SeedingHelpers.UniqueEmail("effective-unapproved"), Password, TestContext.Current.CancellationToken);
        using var response = await client.GetAsync(Route(route, 1), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("current")]
    [InlineData("working")]
    [InlineData("closed")]
    public async Task OrdinaryMemberReadsPopulatedPlacementBodyWithOmittedOptionalQueriesAsync(string route)
    {
        using var client = fixture.CreateNovaHttpClient();
        var member = await RegisterMemberAsync(client);
        var seed = await SeedAsync(member, string.Equals(route, "closed", StringComparison.Ordinal));
        using var response = await client.GetAsync(Route(route, seed.CampaignId), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await AssertSuccessAsync(response, route, seed);
    }

    private static async Task AssertSuccessAsync(HttpResponseMessage response, string route, Seed seed)
    {
        if (string.Equals(route, "current", StringComparison.Ordinal))
        {
            var body = await response.Content.ReadFromJsonAsync<CurrentSeasonRosterResult>(TestContext.Current.CancellationToken);
            body.ShouldNotBeNull();
            body.Season!.SeasonId.ShouldBe(seed.SeasonId);
            body.Roster.Page.ShouldBe(1);
            body.Roster.PageSize.ShouldBe(50);
            body.Roster.TotalCount.ShouldBe(1);
            AssertSource(body.Roster.Items.ShouldHaveSingleItem().Source, seed);
        }
        else if (string.Equals(route, "working", StringComparison.Ordinal))
        {
            var body = await response.Content.ReadFromJsonAsync<CampaignEffectivePlacementsResult>(TestContext.Current.CancellationToken);
            body.ShouldNotBeNull();
            body.Campaign.CampaignId.ShouldBe(seed.CampaignId);
            body.Campaign.Status.ShouldBe(CampaignStatus.Active);
            body.Participants.Page.ShouldBe(1);
            body.Participants.PageSize.ShouldBe(50);
            body.Participants.TotalCount.ShouldBe(3);
            body.Counts.ShouldBe(new EffectivePlacementCounts(2, 1, 0, 0));
            var assigned = body.Participants.Items.Single(row => row.TryoutNumber == 1);
            AssertSource(assigned.EffectiveDecision!, seed);
            assigned.LocalDecision.ShouldBe(assigned.EffectiveDecision!.Decision);
            assigned.EffectiveTeam!.TeamId.ShouldBe(seed.TeamId);
            assigned.ConcurrencyToken.ShouldBe(assigned.LocalDecision!.ConcurrencyToken);
            var undecided = body.Participants.Items.Single(row => row.TryoutNumber == 2);
            undecided.LocalDecision.ShouldBeNull();
            undecided.EffectiveDecision.ShouldBeNull();
            undecided.Eligibility.ShouldBe(EffectivePlacementEligibility.NeedsPlacement);
        }
        else
        {
            var body = await response.Content.ReadFromJsonAsync<ClosedCampaignRosterResult>(TestContext.Current.CancellationToken);
            body.ShouldNotBeNull();
            body.Campaign.CampaignId.ShouldBe(seed.CampaignId);
            body.Campaign.Status.ShouldBe(CampaignStatus.Closed);
            body.Campaign.Season.SeasonId.ShouldBe(seed.SeasonId);
            body.Participants.Page.ShouldBe(1);
            body.Participants.PageSize.ShouldBe(50);
            body.Participants.TotalCount.ShouldBe(3);
            AssertSource(body.Participants.Items.Single(row => row.TryoutNumber == 1).Source, seed);
            body.Participants.Items.Count(row => row.Source.Decision.Outcome == PlacementOutcome.NotSelected).ShouldBe(2);
        }
    }

    [Fact]
    public async Task CurrentRosterWithoutSeasonReturnsExplicitNullAndEmptyPageAsync()
    {
        using var client = fixture.CreateNovaHttpClient();
        _ = await RegisterMemberAsync(client);
        using var response = await client.GetAsync(Route("current", 1), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CurrentSeasonRosterResult>(TestContext.Current.CancellationToken);
        body.ShouldNotBeNull();
        body.Season.ShouldBeNull();
        body.Roster.TotalCount.ShouldBe(0);
        body.Roster.Items.ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("current")]
    [InlineData("working")]
    [InlineData("closed")]
    public async Task PlacementReadsRejectInvalidExplicitPagingWithTraceBearingValidationAsync(string route)
    {
        using var client = fixture.CreateNovaHttpClient();
        var member = await RegisterMemberAsync(client);
        var seed = await SeedAsync(member, string.Equals(route, "closed", StringComparison.Ordinal));
        foreach (var query in new[] { "page=0", "pageSize=0", "pageSize=101", "page=2147483647&pageSize=100", "page=invalid" })
        {
            using var response = await client.GetAsync(Route(route, seed.CampaignId, query), TestContext.Current.CancellationToken);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("current")]
    [InlineData("working")]
    public async Task PlacementReadsRejectInvalidFiltersAndHideForeignTeamsAsync(string route)
    {
        using var client = fixture.CreateNovaHttpClient();
        var member = await RegisterMemberAsync(client);
        var seed = await SeedAsync(member, closed: false);
        foreach (var query in new[] { "teamId=0", "graduationYear=0", $"search={new string('a', 201)}" })
        {
            using var response = await client.GetAsync(Route(route, seed.CampaignId, query), TestContext.Current.CancellationToken);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }
        using var otherClient = fixture.CreateNovaHttpClient();
        var other = await RegisterMemberAsync(otherClient);
        var otherSeed = await SeedAsync(other, closed: false);
        using var foreign = await client.GetAsync(Route(route, seed.CampaignId, $"teamId={otherSeed.TeamId}"), TestContext.Current.CancellationToken);
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("working")]
    [InlineData("closed")]
    public async Task CampaignReadHidesForeignAndDraftCampaignsAndReportsVisibleLifecycleConflictAsync(string route)
    {
        using var client = fixture.CreateNovaHttpClient();
        var member = await RegisterMemberAsync(client);
        var seed = await SeedAsync(member, closed: string.Equals(route, "working", StringComparison.Ordinal));
        using var wrongLifecycle = await client.GetAsync(Route(route, seed.CampaignId), TestContext.Current.CancellationToken);
        await AssertProblemAsync(wrongLifecycle, HttpStatusCode.Conflict);

        using var outsider = fixture.CreateNovaHttpClient();
        _ = await RegisterMemberAsync(outsider);
        using var foreign = await outsider.GetAsync(Route(route, seed.CampaignId), TestContext.Current.CancellationToken);
        await AssertProblemAsync(foreign, HttpStatusCode.NotFound);

        await using var db = fixture.CreateAdminContext();
        var draft = new CampaignEntity
        {
            Name = "Private Draft",
            CreationOperationId = Guid.NewGuid(),
            ClubId = member.ClubId,
            SeasonId = seed.SeasonId,
            Status = CampaignStatus.Draft,
            CreatedById = 1,
        };
        db.Campaigns.Add(draft);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var hidden = await client.GetAsync(Route(route, draft.CampaignId), TestContext.Current.CancellationToken);
        await AssertProblemAsync(hidden, HttpStatusCode.NotFound);
    }

    private async Task<Member> RegisterMemberAsync(HttpClient client)
    {
        var email = SeedingHelpers.UniqueEmail("effective-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, TestContext.Current.CancellationToken);
        await using var db = fixture.CreateAdminContext();
        var club = new ClubEntity
        {
            Name = $"Member read {Guid.NewGuid():N}",
            CreationOperationId = Guid.NewGuid(),
            City = "Austin",
            State = "TX",
            CreatedById = 1,
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, email, club.ClubId, TestContext.Current.CancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, TestContext.Current.CancellationToken);
        return new(club.ClubId, email);
    }

    private async Task<Seed> SeedAsync(Member member, bool closed)
    {
        var seed = await SeedingHelpers.SeedCampaignWithParticipantsAsync(fixture, member.ClubId, member.Email, "Effective", 3,
            closed ? PlacementOutcome.NotSelected : PlacementOutcome.Undecided, TestContext.Current.CancellationToken);
        var teamId = await SeedingHelpers.InsertTeamAsync(fixture, member.ClubId, member.Email, "Effective team", 2030, TestContext.Current.CancellationToken);
        await SeedingHelpers.AssignPlacementAsync(fixture, seed.AssignmentIds[0], teamId, TestContext.Current.CancellationToken);
        await using var db = fixture.CreateAdminContext();
        var campaign = await db.Campaigns.SingleAsync(c => c.CampaignId == seed.CampaignId, TestContext.Current.CancellationToken);
        if (closed)
        {
            campaign.Status = CampaignStatus.Closed;
            campaign.ClosedAt = DateTimeOffset.UtcNow;
            campaign.ClosedById = campaign.CreatedById;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        return new(seed.CampaignId, campaign.SeasonId, teamId, seed.AssignmentIds[0]);
    }

    // Deliberately omit the builder's paging defaults to exercise optional minimal-API query binding.
    private static Uri Route(string route, long campaignId, string? query = null)
    {
        var path = (route switch
        {
            "current" => SeasonEndpoints.CurrentRosterUrl(new()),
            "working" => CampaignEndpoints.EffectivePlacementsUrl(new() { CampaignId = campaignId }),
            "closed" => CampaignEndpoints.ClosedRosterUrl(new() { CampaignId = campaignId }),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        }).Split('?')[0];
        return new Uri(query is null ? path : $"{path}?{query}", UriKind.Relative);
    }

    private static void AssertSource(PlacementDecisionSource source, Seed seed)
    {
        source.ShouldNotBeNull();
        source.Decision.CampaignId.ShouldBe(seed.CampaignId);
        source.Decision.SeasonId.ShouldBe(seed.SeasonId);
        source.Decision.PlayerCampaignAssignmentId.ShouldBe(seed.AssignmentId);
        source.Decision.Outcome.ShouldBe(PlacementOutcome.Assigned);
        source.Decision.TeamId.ShouldBe(seed.TeamId);
        source.Team!.TeamId.ShouldBe(seed.TeamId);
        source.Decision.ConcurrencyToken.ShouldNotBe(Guid.Empty);
        source.Decision.RecordedAt.ShouldBeGreaterThan(DateTimeOffset.MinValue);
        source.Decision.RecordedById.ShouldBeGreaterThan(0);
        source.Decision.ActorDisplayName.ShouldNotBeNullOrWhiteSpace();
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.ShouldBe(expected);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Status.ShouldBe((int)expected);
        problem.Extensions.ShouldContainKey("traceId");
        problem.Extensions["traceId"]!.ToString()!.Length.ShouldBe(32);
    }

    private sealed record Member(long ClubId, string Email);
    private sealed record Seed(long CampaignId, long SeasonId, long TeamId, long AssignmentId);
}
