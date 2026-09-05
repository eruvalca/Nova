using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Nova.Client.Services.Activity;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Activity;

/// <summary>Verifies supersession decisions retain coherent source and resulting state over HTTP.</summary>
public sealed partial class HttpClubActivityQueryServiceTests
{
    /// <summary>All pairs of saved outcomes are valid superseding decisions.</summary>
    /// <param name="previous">The source decision.</param>
    /// <param name="outcome">The new decision.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 3)]
    public async Task GetClubActivityAsync_AcceptsSupersession_ForSavedOutcomes(int previous, int outcome)
    {
        var context = SupersessionContext() with
        {
            PreviousOutcome = (PlacementOutcome)previous,
            PreviousTeamId = previous == 1 ? 5 : null,
            PreviousTeamName = previous == 1 ? "Blue" : null,
            Outcome = (PlacementOutcome)outcome,
            TeamId = outcome == 1 ? 5 : null,
            TeamName = outcome == 1 ? "Blue" : null,
        };
        var result = await ReadSupersessionAsync(context);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Single().Context.ShouldBe(context);
    }

    /// <summary>Each malformed source or decision field rejects the successful HTTP body.</summary>
    /// <param name="property">The field to corrupt.</param>
    /// <param name="json">The invalid JSON value.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("playerId", "null")]
    [InlineData("playerId", "0")]
    [InlineData("seasonId", "null")]
    [InlineData("seasonId", "-1")]
    [InlineData("previousCampaignId", "null")]
    [InlineData("previousCampaignId", "0")]
    [InlineData("previousCampaignId", "20")]
    [InlineData("previousCampaignName", "null")]
    [InlineData("previousCampaignName", "\" \"")]
    [InlineData("previousPlayerCampaignAssignmentId", "null")]
    [InlineData("previousPlayerCampaignAssignmentId", "0")]
    [InlineData("previousPlayerCampaignAssignmentId", "200")]
    [InlineData("previousOutcome", "null")]
    [InlineData("previousOutcome", "0")]
    [InlineData("previousOutcome", "99")]
    [InlineData("previousOutcome", "2")]
    [InlineData("previousTeamId", "null")]
    [InlineData("previousTeamId", "0")]
    [InlineData("previousTeamName", "null")]
    [InlineData("previousTeamName", "\" \"")]
    [InlineData("outcome", "0")]
    [InlineData("outcome", "99")]
    [InlineData("outcome", "3")]
    [InlineData("teamId", "null")]
    [InlineData("teamId", "0")]
    [InlineData("teamName", "null")]
    [InlineData("teamName", "\" \"")]
    public async Task GetClubActivityAsync_RejectsSupersession_ForMalformedDecision(string property, string json)
    {
        var result = await ReadSupersessionAsync(SupersessionContext(), property, json);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Creates a same-team supersession with distinct owning campaigns and participations.</summary>
    /// <returns>The complete saved-decision snapshots.</returns>
    private static PlacementContext SupersessionContext() => new()
    {
        PlayerId = 1,
        SeasonId = 2,
        PreviousCampaignId = 10,
        PreviousCampaignName = "Earlier",
        PreviousPlayerCampaignAssignmentId = 100,
        CampaignId = 20,
        CampaignName = "Later",
        PlayerCampaignAssignmentId = 200,
        PlayerDisplayName = "Avery",
        PreviousOutcome = PlacementOutcome.Assigned,
        PreviousTeamId = 5,
        PreviousTeamName = "Blue",
        Outcome = PlacementOutcome.Assigned,
        TeamId = 5,
        TeamName = "Blue",
    };

    /// <summary>Round-trips a feed through the real HTTP validator, optionally corrupting one field.</summary>
    /// <param name="context">The source payload.</param>
    /// <param name="property">An optional field to corrupt.</param>
    /// <param name="json">The replacement JSON value.</param>
    /// <returns>The client result.</returns>
    private static async Task<ServiceResult<ClubActivityResult>> ReadSupersessionAsync(
        PlacementContext context, string? property = null, string? json = null)
    {
        var item = NewItem(1, ActivityEventKind.PlacementSuperseded, DateTimeOffset.UtcNow) with { Context = context };
        using var content = JsonContent.Create(new ClubActivityResult([item], false, null));
        var body = JsonNode.Parse(await content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        if (property is not null)
        {
            body["events"]![0]!["context"]![property] = JsonNode.Parse(json!);
        }
        using var http = new HttpClient(new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
        })))
        { BaseAddress = new Uri("https://example.com") };
        return await new HttpClubActivityQueryService(http).GetClubActivityAsync(new(), TestContext.Current.CancellationToken);
    }
}
