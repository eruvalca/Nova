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
            "inaccessible" => source with { Team = null },
            _ => source
        };
        var previous = new PreviousSeasonPlacement(new PlacementSeasonIdentity(5, "Prior season"), source,
            !string.Equals(shape, "inaccessible", StringComparison.Ordinal) && !string.Equals(shape, "zeroDecisionTeamId", StringComparison.Ordinal));
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
