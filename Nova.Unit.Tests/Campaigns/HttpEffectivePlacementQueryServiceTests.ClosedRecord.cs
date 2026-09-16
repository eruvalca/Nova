using System.Text.Json.Nodes;
using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class HttpEffectivePlacementQueryServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task ClosedRecordBoundsUndecidedTotalEvenOnEmptyOutOfRangePageAsync(int filteredTotal, bool valid)
    {
        var payload = Payload(2);
        payload["participants"]!["items"] = new JsonArray();
        payload["participants"]!["page"] = 2;
        payload["participants"]!["pageSize"] = 1;
        payload["participants"]!["totalCount"] = filteredTotal;
        using var handler = new RecordingHandler(_ => Response(payload.ToJsonString()));
        using var http = CreateHttp(handler);
        var result = await new HttpEffectivePlacementQueryService(http).GetClosedCampaignRosterAsync(new()
        {
            CampaignId = 42,
            Page = 2,
            PageSize = 1,
            LocalOutcome = "undecided",
        }, TestContext.Current.CancellationToken);
        if (valid)
        {
            result.IsSuccess.ShouldBeTrue();
            result.Value.Participants.TotalCount.ShouldBe(0);
            result.Value.Participants.Items.ShouldBeEmpty();
        }
        else { result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError); }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Assigned)]
    [InlineData(PlacementOutcome.NotSelected)]
    [InlineData(PlacementOutcome.Withdrawn)]
    public async Task ClosedRecordRejectsBalancedSummaryContradictingReturnedOutcomeAsync(PlacementOutcome outcome)
    {
        var payload = ClosedOutcomePayload(outcome, 0, 1);
        (await ReadPayloadAsync(2, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Assigned, 3, false)]
    [InlineData(PlacementOutcome.NotSelected, 3, false)]
    [InlineData(PlacementOutcome.Withdrawn, 3, false)]
    [InlineData(PlacementOutcome.Assigned, 2, true)]
    [InlineData(PlacementOutcome.NotSelected, 2, true)]
    [InlineData(PlacementOutcome.Withdrawn, 2, true)]
    [InlineData(PlacementOutcome.Assigned, 1, true)]
    [InlineData(PlacementOutcome.NotSelected, 1, true)]
    [InlineData(PlacementOutcome.Withdrawn, 1, true)]
    public async Task ClosedRecordBoundsFilteredTotalWithoutEquatingPageOrSearchToWholeSummaryAsync(
        PlacementOutcome outcome, int filteredTotal, bool valid)
    {
        var payload = ClosedOutcomePayload(outcome, 2, 4);
        payload["participants"]!["pageSize"] = 1;
        payload["participants"]!["totalCount"] = filteredTotal;
        using var handler = new RecordingHandler(_ => Response(payload.ToJsonString()));
        using var http = CreateHttp(handler);
        var result = await new HttpEffectivePlacementQueryService(http).GetClosedCampaignRosterAsync(new()
        {
            CampaignId = 42,
            PageSize = 1,
            LocalOutcome = outcome.ToString().ToUpperInvariant(),
            Search = "Zoe",
        }, TestContext.Current.CancellationToken);
        if (valid)
        {
            result.IsSuccess.ShouldBeTrue();
            result.Value.Participants.TotalCount.ShouldBe(filteredTotal);
            result.Value.Summary.TotalCount.ShouldBe(4);
        }
        else { result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError); }
    }

    private static JsonNode ClosedOutcomePayload(PlacementOutcome outcome, int outcomeCount, int total)
    {
        var payload = Payload(2);
        var row = Row(payload, 2);
        row["source"]!["decision"]!["outcome"] = (int)outcome;
        if (outcome != PlacementOutcome.Assigned)
        {
            row["source"]!["decision"]!["teamId"] = null;
            row["source"]!["team"] = null;
            row["teamLifecycleStatus"] = null;
        }
        payload["participantCount"] = total;
        payload["summary"]!["totalCount"] = total;
        payload["summary"]!["assignedCount"] = outcome == PlacementOutcome.Assigned ? outcomeCount : total - outcomeCount;
        payload["summary"]!["notSelectedCount"] = outcome switch
        {
            PlacementOutcome.NotSelected => outcomeCount,
            PlacementOutcome.Assigned => total - outcomeCount,
            _ => 0,
        };
        payload["summary"]!["withdrawnCount"] = outcome == PlacementOutcome.Withdrawn ? outcomeCount : 0;
        return payload;
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("summary")]
    [InlineData("closingEvent")]
    [InlineData("playerLifecycleStatus")]
    [InlineData("teamLifecycleStatus")]
    public async Task ClosedRecordRejectsMissingRequiredSnapshotFieldsAsync(string field)
    {
        var payload = Payload(2);
        var owner = field is "summary" or "closingEvent" ? payload.AsObject() : Row(payload, 2);
        owner.Remove(field).ShouldBeTrue();
        (await ReadPayloadAsync(2, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("totals")]
    [InlineData("undecided")]
    [InlineData("reopened")]
    [InlineData("actor")]
    [InlineData("archive")]
    public async Task ClosedRecordRejectsContradictoryMetadataAsync(string defect)
    {
        var payload = Payload(2);
        switch (defect)
        {
            case "totals": payload["summary"]!["assignedCount"] = 0; break;
            case "undecided": payload["summary"]!["undecidedCount"] = 1; break;
            case "reopened": payload["closingEvent"]!["eventType"] = (int)CampaignLifecycleEventType.Reopened; break;
            case "actor": payload["closingEvent"]!["actorDisplayName"] = " "; break;
            case "archive": Row(payload, 2)["playerLifecycleStatus"] = 99; break;
        }
        (await ReadPayloadAsync(2, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }
}
