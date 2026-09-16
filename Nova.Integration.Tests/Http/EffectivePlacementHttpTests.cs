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

    [Fact]
    public async Task CloseReviewBindsExactBlockersAndStableSqlPagesWithoutNarrowingCountsAsync()
    {
        var token = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var member = await RegisterMemberAsync(client);
        var seed = await SeedAsync(member, closed: false);
        long[] ids;
        await using (var db = fixture.CreateAdminContext())
        {
            var assignments = await db.PlayerCampaignAssignments.Include(row => row.Player)
                .Where(row => row.CampaignId == seed.CampaignId).OrderBy(row => row.PlayerCampaignAssignmentId).ToListAsync(token);
            foreach (var assignment in assignments)
            {
                assignment.Player.FirstName = "Literal_%";
                assignment.Player.LastName = "Duplicate";
            }
            ids = assignments.Select(row => row.PlayerCampaignAssignmentId).ToArray();
            await db.SaveChangesAsync(token);
        }
        for (var page = 1; page <= 3; page++)
        {
            using var response = await client.GetAsync(Route("working", seed.CampaignId, $"sortBy=closeout&pageSize=1&page={page}"), token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<CampaignEffectivePlacementsResult>(token);
            body!.Participants.Items.ShouldHaveSingleItem().PlayerCampaignAssignmentId.ShouldBe(ids[page - 1]);
            body.Participants.TotalCount.ShouldBe(3);
        }
        using var filtered = await client.GetAsync(Route("working", seed.CampaignId, "sortBy=closeout&closeoutBlocker=outcomes&search=Literal_%25&pageSize=1"), token);
        filtered.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await filtered.Content.ReadFromJsonAsync<CampaignEffectivePlacementsResult>(token);
        result!.Participants.TotalCount.ShouldBe(2);
        result.Participants.Items.ShouldHaveSingleItem().PlayerCampaignAssignmentId.ShouldBe(ids[1]);
        result.Counts.ShouldBe(new EffectivePlacementCounts(2, 1, 0, 0));
        foreach (var query in new[] { "closeoutBlocker=unknown", "closeoutBlocker=%20", "sortBy=closeout&sortDirection=desc", "pageSize=101" })
        {
            using var invalid = await client.GetAsync(Route("working", seed.CampaignId, query), token);
            await AssertProblemAsync(invalid, HttpStatusCode.BadRequest);
        }
    }

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

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("working")]
    [InlineData("closed")]
    public async Task OrdinaryMemberCombinesRepeatedDiscoveryFiltersAndReadsUnfilteredScaleAsync(string route)
    {
        var token = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var member = await RegisterMemberAsync(client);
        var seed = await SeedAsync(member, string.Equals(route, "closed", StringComparison.Ordinal));
        var unusedTag = await SeedingHelpers.InsertTagDefinitionAsync(fixture, seed.AssignmentId, member.Email, "Unused", "primary", token);
        var appliedTag = await SeedingHelpers.InsertTagDefinitionAsync(fixture, seed.AssignmentId, member.Email, "Leader", "success", token, archived: true);
        await using (var db = fixture.CreateAdminContext())
        {
            db.CampaignTagApplications.Add(new CampaignTagApplicationEntity
            {
                AuthorDisplayName = "Seeded evaluator",
                PlayerCampaignAssignmentId = seed.AssignmentId,
                PlayerTagId = appliedTag,
                ClubId = member.ClubId,
                CreationOperationId = Guid.NewGuid(),
                CreatedById = 1
            });
            await db.SaveChangesAsync(token);
        }
        var query = $"graduationYears=2040&graduationYears=2031&tagDefinitionIds={unusedTag}&tagDefinitionIds={appliedTag}&localOutcome=assigned&localTeamId={seed.TeamId}&participantId={seed.AssignmentId}&search=1&sortBy=tryoutNumber&sortDirection=desc&page=1&pageSize=1";
        using var response = await client.GetAsync(Route(route, seed.CampaignId, query), token);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        if (string.Equals(route, "working", StringComparison.Ordinal))
        {
            var body = await response.Content.ReadFromJsonAsync<CampaignEffectivePlacementsResult>(token);
            body.ShouldNotBeNull();
            body.Counts.ShouldBe(new EffectivePlacementCounts(2, 1, 0, 0));
            body.Participants.TotalCount.ShouldBe(1);
            var row = body.Participants.Items.ShouldHaveSingleItem();
            row.PlayerCampaignAssignmentId.ShouldBe(seed.AssignmentId);
            row.GraduationYear.ShouldBe(2031);
            row.LocalTeam!.TeamId.ShouldBe(seed.TeamId);
            row.AppliedTags.ShouldHaveSingleItem().ShouldBe(new CampaignParticipantTagSummaryDto(appliedTag, "Leader", "success", true));
        }
        else
        {
            var body = await response.Content.ReadFromJsonAsync<ClosedCampaignRosterResult>(token);
            body.ShouldNotBeNull();
            body.ParticipantCount.ShouldBe(3);
            body.Participants.TotalCount.ShouldBe(1);
            var row = body.Participants.Items.ShouldHaveSingleItem();
            row.PlayerCampaignAssignmentId.ShouldBe(seed.AssignmentId);
            row.GraduationYear.ShouldBe(2031);
            AssertSource(row.Source, seed);
            row.AppliedTags.ShouldHaveSingleItem().ShouldBe(new CampaignParticipantTagSummaryDto(appliedTag, "Leader", "success", true));
        }
        using var noMatch = await client.GetAsync(Route(route, seed.CampaignId, $"participantId={seed.AssignmentId}&localOutcome=withdrawn"), token);
        noMatch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await noMatch.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(token);
        json!["participants"]!["totalCount"]!.GetValue<int>().ShouldBe(0);
        json["participants"]!["items"]!.AsArray().ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("working", null)]
    [InlineData("working", "asc")]
    [InlineData("working", "desc")]
    [InlineData("closed", null)]
    [InlineData("closed", "asc")]
    [InlineData("closed", "desc")]
    public async Task DirectionOnlyDiscoveryOrdersSqlPagesByNameWhileOmittedSortRetainsLifecycleDefaultAsync(string route, string? direction)
    {
        var token = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var member = await RegisterMemberAsync(client);
        var seed = await SeedAsync(member, string.Equals(route, "closed", StringComparison.Ordinal));
        long[] assignmentIds;
        await using (var db = fixture.CreateAdminContext())
        {
            var assignments = await db.PlayerCampaignAssignments.Include(assignment => assignment.Player)
                .Where(assignment => assignment.CampaignId == seed.CampaignId)
                .OrderBy(assignment => assignment.TryoutNumber).ToListAsync(token);
            assignments.Count.ShouldBe(3);
            assignmentIds = assignments.Select(assignment => assignment.PlayerCampaignAssignmentId).ToArray();
            assignments[0].Player.FirstName = "Alex";
            assignments[0].Player.LastName = "Zulu";
            assignments[1].Player.FirstName = "Zoe";
            assignments[1].Player.LastName = "Able";
            assignments[2].Player.FirstName = "Blake";
            assignments[2].Player.LastName = "Middle";
            assignments.Select(assignment => assignment.Player.GraduationYear).ShouldBe([2031, 2032, 2030]);
            await db.SaveChangesAsync(token);
        }
        int[] expectedIndexes = direction switch
        {
            "desc" => [0, 2, 1],
            null when string.Equals(route, "working", StringComparison.Ordinal) => [2, 0, 1],
            _ => [1, 2, 0],
        };

        for (var page = 1; page <= expectedIndexes.Length; page++)
        {
            var query = $"page={page}&pageSize=1";
            if (direction is not null) { query += $"&sortDirection={direction}"; }
            using var response = await client.GetAsync(Route(route, seed.CampaignId, query), token);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(token);
            body.ShouldNotBeNull();
            var participants = body["participants"]!;
            participants["page"]!.GetValue<int>().ShouldBe(page);
            participants["pageSize"]!.GetValue<int>().ShouldBe(1);
            participants["totalCount"]!.GetValue<int>().ShouldBe(3);
            participants["items"]!.AsArray().ShouldHaveSingleItem()!["playerCampaignAssignmentId"]!.GetValue<long>()
                .ShouldBe(assignmentIds[expectedIndexes[page - 1]]);
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("working")]
    [InlineData("closed")]
    public async Task DiscoveryRejectsInvalidExplicitValuesAndForeignIdentifiersAsync(string route)
    {
        var token = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var member = await RegisterMemberAsync(client);
        var seed = await SeedAsync(member, string.Equals(route, "closed", StringComparison.Ordinal));
        foreach (var query in new[]
        {
            "graduationYears=2030&graduationYears=0", "graduationYears=invalid", "tagDefinitionIds=1&tagDefinitionIds=-1",
            "tagDefinitionIds=invalid", "localTeamId=0", "participantId=0", "localOutcome=unknown", "sortBy=unknown", "sortDirection=sideways"
        })
        {
            using var response = await client.GetAsync(Route(route, seed.CampaignId, query), token);
            await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        }
        using var otherClient = fixture.CreateNovaHttpClient();
        var other = await RegisterMemberAsync(otherClient);
        var otherSeed = await SeedAsync(other, string.Equals(route, "closed", StringComparison.Ordinal));
        var otherTag = await SeedingHelpers.InsertTagDefinitionAsync(fixture, otherSeed.AssignmentId, other.Email, "Foreign", "primary", token);
        foreach (var query in new[] { $"localTeamId={otherSeed.TeamId}", $"tagDefinitionIds={otherTag}" })
        {
            using var response = await client.GetAsync(Route(route, seed.CampaignId, query), token);
            await AssertProblemAsync(response, HttpStatusCode.NotFound);
        }
        using var foreignParticipant = await client.GetAsync(Route(route, seed.CampaignId, $"participantId={otherSeed.AssignmentId}"), token);
        foreignParticipant.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await foreignParticipant.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(token);
        json!["participants"]!["totalCount"]!.GetValue<int>().ShouldBe(0);
        json["participants"]!["items"]!.AsArray().ShouldBeEmpty();
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
