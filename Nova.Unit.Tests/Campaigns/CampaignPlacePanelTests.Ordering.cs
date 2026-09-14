using Bunit;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Ordering tests for the Place destination: an obsolete read must never replace the participant the sheet,
/// the highlighted row, and the URL all agree on, because the decision controls submit against the sheet.
/// </summary>
public sealed partial class CampaignPlacePanelTests
{
    [Fact]
    public async Task ASelectionReadLandingAfterTheSelectionChangedNeverAdoptsTheOldParticipantAsync()
    {
        var gate = new TaskCompletionSource();
        var queries = RegisterServices(rows: [CreateRow(301)]);

        // The linked participant is not on the loaded page, so the sheet resolves them with an exact read
        // that this test holds open until a newer selection has already been adopted.
        queries.GetCampaignEffectivePlacementsAsync(
                Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == 302),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await gate.Task;
                var obsolete = CreateRow(302) with { FirstName = "Obsolete", LastName = "Selection" };
                return new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult([obsolete], 1));
            });

        var cut = RenderPanel(selectedParticipantId: 302);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Loading placement evidence"));

        // The member moves to a participant already on the loaded page while that read is still in flight.
        cut.Render(parameters => parameters.Add(component => component.SelectedParticipantId, 301L));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Chen"));

        // The obsolete read now resolves. It must be discarded rather than adopted, or "Save placement"
        // would record a decision against a participant the sheet no longer shows.
        gate.SetResult();
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);

        cut.Markup.ShouldNotContain("Obsolete Selection");
        cut.Markup.ShouldContain("Avery Chen");
    }
}
