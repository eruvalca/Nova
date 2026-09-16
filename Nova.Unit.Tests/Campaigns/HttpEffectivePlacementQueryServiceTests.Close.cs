using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class HttpEffectivePlacementQueryServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("eligibility", PlacementCorrectionReason.None, false)]
    [InlineData("eligibility", PlacementCorrectionReason.TeamUnavailable, true)]
    [InlineData("eligibility", PlacementCorrectionReason.TeamIncompatible, true)]
    [InlineData("eligibility", PlacementCorrectionReason.TeamArchived, true)]
    [InlineData("archivedTeams", PlacementCorrectionReason.None, false)]
    [InlineData("archivedTeams", PlacementCorrectionReason.TeamUnavailable, false)]
    [InlineData("archivedTeams", PlacementCorrectionReason.TeamIncompatible, false)]
    [InlineData("archivedTeams", PlacementCorrectionReason.TeamArchived, true)]
    public async Task CloseBlockerRejectsDisprovenMembershipAndRetainsPossibleOverlapAsync(
        string blocker, PlacementCorrectionReason reason, bool accepted)
    {
        var payload = Payload(1);
        var row = Row(payload, 1);
        row["correctionReason"] = (int)reason;
        if (reason != PlacementCorrectionReason.None)
        {
            row["eligibility"] = (int)EffectivePlacementEligibility.NeedsPlacement;
            row["effectiveTeam"] = null;
            payload["counts"]!["needsPlacement"] = 1;
            payload["counts"]!["optionalReassignment"] = 0;
        }
        if (reason == PlacementCorrectionReason.TeamUnavailable)
        {
            row["localTeam"] = null;
            row["localDecision"]!["teamId"] = null;
            row["effectiveDecision"]!["team"] = null;
            row["effectiveDecision"]!["decision"]!["teamId"] = null;
        }
        using var handler = new RecordingHandler(_ => Response(payload.ToJsonString()));
        using var http = CreateHttp(handler);
        var service = new HttpEffectivePlacementQueryService(http);
        var result = await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = 42, CloseoutBlocker = blocker }, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBe(accepted);
        if (!accepted) { result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError); }
        // The payload itself remains valid without the condition-keyed restriction.
        (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = 42 }, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
    }
}
