using Bunit;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Ordering tests for the Place destination: an obsolete read must never replace the participant the sheet,
/// the highlighted row, and the URL all agree on, because the decision controls submit against the sheet.
/// </summary>
/// <remarks>
/// Every participant in these tests carries a distinct name on purpose. Assertions read the working sheet's
/// own <c>.place-name</c> element rather than the whole markup, because the queue renders participant names
/// too and a whole-markup assertion would pass without the sheet ever resolving anything.
/// </remarks>
public sealed partial class CampaignPlacePanelTests
{
    [Fact]
    public async Task ASelectionReadLandingAfterTheSelectionChangedNeverAdoptsTheOldParticipantAsync()
    {
        var gate = new TaskCompletionSource();
        var queries = RegisterServices(rows: [Row(301, "Row", "Onepage")]);
        GateParticipantRead(queries, 302, gate, Row(302, "Stale", "Earlier"));

        // Resolve a participant from the loaded page first so the panel has finished initializing, then
        // select a participant who is not on the page and hold their exact read open.
        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => SheetName(cut).ShouldBe("Row Onepage"));
        ReRenderSelected(cut, 302L);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Loading placement evidence"));

        // The member moves back to a participant already on the loaded page while that read is in flight.
        ReRenderSelected(cut, 301L);
        await cut.WaitForAssertionAsync(() => SheetName(cut).ShouldBe("Row Onepage"));

        // The obsolete read now resolves. It must be discarded rather than adopted, or "Save placement"
        // would record a decision against a participant the sheet no longer shows.
        gate.SetResult();
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);

        SheetName(cut).ShouldBe("Row Onepage");
        cut.Markup.ShouldNotContain("Stale Earlier");
    }

    [Fact]
    public async Task AnObsoleteReadIsDiscardedWhenTheSelectionMovesToAnotherExactReadAsync()
    {
        // Both participants need their own exact read, so the second selection supersedes the first while
        // the first read is still in flight rather than resolving from the loaded page.
        var firstGate = new TaskCompletionSource();
        var queries = RegisterServices(rows: [Row(301, "Row", "Onepage")]);
        GateParticipantRead(queries, 302, firstGate, Row(302, "Stale", "Earlier"));
        queries.GetCampaignEffectivePlacementsAsync(
                Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == 303),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ServiceResult<CampaignEffectivePlacementsResult>(
                CreateEffectiveResult([Row(303, "Newer", "Exact")], 1))));

        var cut = RenderPanel(selectedParticipantId: 301);
        await cut.WaitForAssertionAsync(() => SheetName(cut).ShouldBe("Row Onepage"));
        ReRenderSelected(cut, 302L);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Loading placement evidence"));

        ReRenderSelected(cut, 303L);
        await cut.WaitForAssertionAsync(() => SheetName(cut).ShouldBe("Newer Exact"));

        // The superseded read now resolves and must not replace the participant the URL selects.
        firstGate.SetResult();
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);

        SheetName(cut).ShouldBe("Newer Exact");
        cut.Markup.ShouldNotContain("Stale Earlier");
    }

    /// <summary>Creates a distinguishable participant row.</summary>
    private static CampaignEffectivePlacementItem Row(long assignmentId, string first, string last)
        => CreateRow(assignmentId) with { FirstName = first, LastName = last };

    /// <summary>Holds one participant's exact read open until the returned source is released.</summary>
    private static void GateParticipantRead(
        IEffectivePlacementQueryService queries, long participantId, TaskCompletionSource gate, CampaignEffectivePlacementItem row)
        => queries.GetCampaignEffectivePlacementsAsync(
                Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == participantId),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await gate.Task;
                return new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult([row], 1));
            });

    /// <summary>Re-renders the panel with every parameter supplied, as a parent navigation would.</summary>
    private static void ReRenderSelected(IRenderedComponent<Nova.UI.Features.Campaigns.Components.CampaignPlacePanel> cut, long? selectedParticipantId)
        => cut.Render(parameters =>
        {
            parameters.Add(component => component.CampaignId, 10);
            parameters.Add(component => component.CampaignStatus, CampaignStatus.Active);
            parameters.Add(component => component.SelectedParticipantId, selectedParticipantId);
            parameters.Add(component => component.CanEditPlacements, true);
            parameters.Add(component => component.State, new Nova.UI.Features.Campaigns.Services.CampaignWorkspacePlacementState());
            parameters.Add(component => component.GraduationYearChoices, (IReadOnlyList<int>)[2032]);
            parameters.Add(component => component.TagChoices, (IReadOnlyList<TagDefinitionDto>)[]);
            parameters.Add(component => component.CampaignTeamChoices, (IReadOnlyList<TeamRosterItem>)[]);
        });

    /// <summary>Reads the working sheet's participant name, or a marker when no sheet is rendered.</summary>
    private static string SheetName(IRenderedComponent<Nova.UI.Features.Campaigns.Components.CampaignPlacePanel> cut)
    {
        var names = cut.FindAll(".place-name");
        return names.Count == 0 ? "<no sheet>" : names[0].TextContent.Trim();
    }
}
