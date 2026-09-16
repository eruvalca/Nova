using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class EffectivePlacementPostgresTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("closure-actor")]
    [InlineData("closure-timestamp")]
    [InlineData("decision-timestamp")]
    [InlineData("decision-token")]
    [InlineData("decision-tab")]
    [InlineData("decision-whitespace")]
    public async Task ClosedIntegrityRejectsMalformedStoredEvidenceBeforeDiscoveryAsync(string defect)
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(1);
        using var user = fixture.UseUser(1, seed.ClubId, isClubAdmin: false);
        var input = new GetClosedCampaignRosterInput { CampaignId = seed.LatestClosedId, Search = "No matching player" };
        (await CreateService().GetClosedCampaignRosterAsync(input, token)).Value.Participants.Items.ShouldBeEmpty();
        await using var db = fixture.CreateAdminContext();
        var closure = db.ActivityEvents.Where(row => row.CampaignId == seed.LatestClosedId && row.ClubId == seed.ClubId);
        var decisions = db.PlayerCampaignAssignments.Where(row => row.CampaignId == seed.LatestClosedId && row.ClubId == seed.ClubId);
        var whitespace = new string(Enumerable.Range(char.MinValue, char.MaxValue + 1).Select(value => (char)value).Where(char.IsWhiteSpace).ToArray());
        // Direct updates preserve intentionally malformed evidence without weakening database constraints.
        var changed = defect switch
        {
            "closure-actor" => await closure.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ActorUserId, 0), token),
            "closure-timestamp" => await closure.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, default(DateTimeOffset)), token),
            "decision-timestamp" => await decisions.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DecisionRecordedAt, default(DateTimeOffset)), token),
            "decision-token" => await decisions.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyToken, Guid.Empty), token),
            "decision-tab" => await decisions.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DecisionActorDisplayName, "\t"), token),
            "decision-whitespace" => await decisions.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DecisionActorDisplayName, whitespace), token),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        changed.ShouldBe(1);
        foreach (var request in new[] { input, input with { Search = null, Page = 99 }, input with { Search = null } })
        {
            var result = await CreateService().GetClosedCampaignRosterAsync(request, token);
            result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
            result.Problem.Errors.ShouldNotBeNull().ShouldContainKey(ClosedCampaignRecordErrors.Integrity);
        }
    }
}
