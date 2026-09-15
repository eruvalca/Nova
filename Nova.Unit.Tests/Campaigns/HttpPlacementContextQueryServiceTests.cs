using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class HttpPlacementContextQueryServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextClientAcceptsRawBoundaryAfterMalformedRowsAreOmittedAsync(bool emptyPage)
    {
        var history = new PlacementHistoryItem(82, 10, "Summer", null, null, PlacementOutcome.NotSelected,
            null, "Member", DateTimeOffset.UtcNow);
        var payload = new PlacementContextResult(301, null, emptyPage ? [] : [history], 81, false);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        using var handler = new ContextHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlacementContextQueryService(http).GetContextAsync(
            new GetPlacementContextInput { CampaignId = 10, PlayerCampaignAssignmentId = 301, BeforeEventId = 101 }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NextEventId.ShouldBe(81);
        result.Value.History.ShouldBe(payload.History);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, null, true)]
    [InlineData(null, "Unexpected team", false)]
    [InlineData(PlacementOutcome.Undecided, null, false)]
    [InlineData((PlacementOutcome)99, null, false)]
    [InlineData(PlacementOutcome.Assigned, "Prior team", true)]
    [InlineData(PlacementOutcome.Assigned, null, true)]
    [InlineData(PlacementOutcome.Assigned, " ", false)]
    [InlineData(PlacementOutcome.NotSelected, null, true)]
    [InlineData(PlacementOutcome.NotSelected, "Unexpected team", false)]
    [InlineData(PlacementOutcome.Withdrawn, null, true)]
    [InlineData(PlacementOutcome.Withdrawn, "Unexpected team", false)]
    public async Task ContextClientRequiresAConsistentPreviousSavedTransitionAsync(PlacementOutcome? previous, string? teamName, bool valid)
    {
        var history = new PlacementHistoryItem(20, 10, "Summer", previous, teamName, PlacementOutcome.NotSelected,
            null, "Member", DateTimeOffset.UtcNow);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new PlacementContextResult(301, null, [history], null, false))
        };
        using var handler = new ContextHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var actual = await new HttpPlacementContextQueryService(http).GetContextAsync(
            new GetPlacementContextInput { CampaignId = 10, PlayerCampaignAssignmentId = 301 }, TestContext.Current.CancellationToken);

        actual.IsSuccess.ShouldBe(valid);
        if (valid) { actual.Value.History.Single().ShouldBe(history); }
        else { actual.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError); }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("valid")]
    [InlineData("null")]
    [InlineData("missingHistory")]
    [InlineData("participant")]
    [InlineData("count")]
    [InlineData("order")]
    [InlineData("outcome")]
    [InlineData("cursor")]
    [InlineData("assignedMissingTeamName")]
    [InlineData("nonAssignedWithTeamName")]
    public async Task ContextClientRequiresBoundedOrderedHistoryAsync(string shape)
    {
        var history = new PlacementHistoryItem(20, 10, "Summer", null, null, PlacementOutcome.NotSelected,
            null, "Member", DateTimeOffset.UtcNow);
        var result = new PlacementContextResult(301, null, [history], null, false);
        var payload = shape switch
        {
            "valid" => System.Text.Json.JsonSerializer.Serialize(result),
            "null" => "null",
            "missingHistory" => "{\"playerCampaignAssignmentId\":301,\"previousPlacement\":null,\"nextEventId\":null,\"canSupersedeWithdrawal\":false}",
            "participant" => System.Text.Json.JsonSerializer.Serialize(result with { PlayerCampaignAssignmentId = 999 }),
            "count" => System.Text.Json.JsonSerializer.Serialize(result with { History = Enumerable.Range(1, 21).Select(index => history with { EventId = 50 - index }).ToArray() }),
            "order" => System.Text.Json.JsonSerializer.Serialize(result with { History = [history, history with { EventId = 21 }] }),
            "outcome" => System.Text.Json.JsonSerializer.Serialize(result with { History = [history with { Outcome = PlacementOutcome.Undecided }] }),
            "cursor" => System.Text.Json.JsonSerializer.Serialize(result with { NextEventId = 21 }),
            "assignedMissingTeamName" => System.Text.Json.JsonSerializer.Serialize(result with { History = [history with { Outcome = PlacementOutcome.Assigned }] }),
            "nonAssignedWithTeamName" => System.Text.Json.JsonSerializer.Serialize(result with { History = [history with { TeamName = "Blue" }] }),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        using var handler = new ContextHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var actual = await new HttpPlacementContextQueryService(http).GetContextAsync(
            new GetPlacementContextInput { CampaignId = 10, PlayerCampaignAssignmentId = 301 }, TestContext.Current.CancellationToken);

        if (string.Equals(shape, "valid", StringComparison.Ordinal))
        {
            actual.IsSuccess.ShouldBeTrue();
            actual.Value.History.Single().ShouldBe(history);
        }
        else
        {
            actual.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }
        handler.RequestUri!.AbsolutePath.ShouldBe("/api/campaigns/10/participants/301/placement-context");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("valid", true)]
    [InlineData("inaccessible", true)]
    [InlineData("inaccessibleCanKeep", false)]
    [InlineData("missingTeamEvidence", false)]
    [InlineData("missingTeamEvidenceCanKeep", false)]
    [InlineData("visibleCannotKeep", true)]
    [InlineData("negativeDecisionTeamId", false)]
    [InlineData("blankTeamName", false)]
    [InlineData("blankCampaignName", false)]
    [InlineData("zeroTeamId", false)]
    [InlineData("missingDecisionTeamId", false)]
    [InlineData("mismatchedTeamId", false)]
    [InlineData("zeroDecisionTeamId", false)]
    public async Task ContextClientRequiresConsistentPriorAssignmentEvidenceAsync(string shape, bool valid)
    {
        var decision = new CampaignSavedPlacementDecision(201, 30, 8, 5, 1, PlacementOutcome.Assigned, 90,
            DateTimeOffset.UtcNow.AddDays(-1), 7, "Prior author", Guid.NewGuid());
        var source = new PlacementDecisionSource(decision, "Prior campaign", new CampaignParticipantTeamSummaryDto(90, "Blue"));
        source = shape switch
        {
            "blankTeamName" => source with { Team = new CampaignParticipantTeamSummaryDto(90, " ") },
            "blankCampaignName" => source with { CampaignName = " " },
            "zeroTeamId" => source with { Team = new CampaignParticipantTeamSummaryDto(0, "Blue"), Decision = decision with { TeamId = 0 } },
            "missingDecisionTeamId" => source with { Decision = decision with { TeamId = null } },
            "mismatchedTeamId" => source with { Decision = decision with { TeamId = 91 } },
            "zeroDecisionTeamId" => source with { Team = null, Decision = decision with { TeamId = 0 } },
            "negativeDecisionTeamId" => source with { Team = null, Decision = decision with { TeamId = -1 } },
            "missingTeamEvidence" or "missingTeamEvidenceCanKeep" => source with { Team = null },
            "inaccessible" or "inaccessibleCanKeep" => source with { Team = null, Decision = decision with { TeamId = null } },
            _ => source
        };
        var previous = new PreviousSeasonPlacement(new PlacementSeasonIdentity(5, "Prior season"), source,
            shape is not ("inaccessible" or "zeroDecisionTeamId" or "negativeDecisionTeamId" or "missingTeamEvidence" or "visibleCannotKeep"));
        var payload = new PlacementContextResult(301, previous, [], null, false);
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        using var handler = new ContextHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var actual = await new HttpPlacementContextQueryService(http).GetContextAsync(
            new GetPlacementContextInput { CampaignId = 10, PlayerCampaignAssignmentId = 301 }, TestContext.Current.CancellationToken);

        actual.IsSuccess.ShouldBe(valid);
        if (valid) { actual.Value.PreviousPlacement.ShouldBe(previous); }
        else { actual.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError); }
    }

    private sealed class ContextHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(response);
        }
    }
}
