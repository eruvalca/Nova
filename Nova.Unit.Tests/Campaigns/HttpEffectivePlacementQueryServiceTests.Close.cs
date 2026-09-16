using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class HttpEffectivePlacementQueryServiceTests
{
    [Theory]
    [InlineData("unknown")]
    [InlineData(" ")]
    [InlineData(" outcomes ")]
    public async Task InvalidCloseBlockerIsRejectedWithoutSendingOrBroadeningRequestAsync(string blocker)
    {
        var requests = 0;
        using var handler = new RecordingHandler(_ => { requests++; return Response(Payload(1).ToJsonString()); });
        using var http = CreateHttp(handler);
        var service = new HttpEffectivePlacementQueryService(http);
        var input = new GetCampaignEffectivePlacementsInput { CampaignId = 42, CloseoutBlocker = blocker };
        CampaignEndpoints.EffectivePlacementsUrl(input).ShouldContain("closeoutBlocker=" + Uri.EscapeDataString(blocker));
        var result = await service.GetCampaignEffectivePlacementsAsync(input, TestContext.Current.CancellationToken);
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.ShouldContainKey(nameof(input.CloseoutBlocker));
        requests.ShouldBe(0);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(2, true, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, true)]
    [InlineData(2, false, true)]
    [InlineData(2, true, true)]
    public async Task CloseoutValidatesNumericTeamAndExactNameParticipantTiesAsync(int endpoint, bool reversed, bool sameTeam)
    {
        var payload = TwoRowPayload(endpoint);
        var rows = payload["participants"]!["items"]!.AsArray();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index]!;
            var teamId = index == 0 || sameTeam ? 2 : 10;
            row[SourceName(endpoint)]!["team"]!["teamId"] = teamId;
            row[SourceName(endpoint)]!["decision"]!["teamId"] = teamId;
            if (!sameTeam) { row["lastName"] = index == 0 ? "Zulu" : "Alpha"; }
            if (endpoint == 1)
            {
                row["localTeam"]!["teamId"] = teamId;
                row["effectiveTeam"]!["teamId"] = teamId;
                row["localDecision"]!["teamId"] = teamId;
            }
        }
        if (reversed) { ReverseRows(payload); }
        using var handler = new RecordingHandler(_ => Response(payload.ToJsonString()));
        using var http = CreateHttp(handler);
        var service = new HttpEffectivePlacementQueryService(http);
        CampaignRosterDiscoveryInput input = endpoint == 1
            ? new GetCampaignEffectivePlacementsInput { CampaignId = 42, SortBy = "closeout" }
            : new GetClosedCampaignRosterInput { CampaignId = 42, SortBy = "closeout" };
        var result = await ReadDiscoveryAsync(service, input);
        result.IsSuccess.ShouldBe(!reversed);
        if (reversed) { result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError); }
    }

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
