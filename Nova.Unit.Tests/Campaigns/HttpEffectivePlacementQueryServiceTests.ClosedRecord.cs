using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class HttpEffectivePlacementQueryServiceTests
{
    [Theory]
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

    [Theory]
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
