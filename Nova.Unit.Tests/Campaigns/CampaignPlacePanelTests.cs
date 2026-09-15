using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;
using CampaignPlacePanel = Nova.UI.Features.Campaigns.Components.CampaignPlacePanel;
using CampaignWorkspacePlacementState = Nova.UI.Features.Campaigns.Services.CampaignWorkspacePlacementState;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Component-level tests for the Place destination: the queue's foundation truth, the selected participant's
/// evidence, the Active/Closed posture split, and each region's own written state.
/// </summary>
public sealed partial class CampaignPlacePanelTests : BunitContext
{
    private readonly BunitJSModuleInterop _placementStorage;

    public CampaignPlacePanelTests()
    {
        _placementStorage = JSInterop.SetupModule("./_content/Nova.UI/Features/Campaigns/Components/CampaignPlacePanel.razor.js");
        _placementStorage.Mode = JSRuntimeMode.Loose;
        _placementStorage.Setup<Nova.UI.Features.Campaigns.Components.PlacementRecoveryRead>("readRecovery", _ => true).SetResult(new(null, null));
        var context = Substitute.For<IPlacementContextQueryService>();
        context.GetContextAsync(Arg.Any<GetPlacementContextInput>(), Arg.Any<CancellationToken>())
            .Returns(call => new ServiceResult<PlacementContextResult>(new PlacementContextResult(
                call.Arg<GetPlacementContextInput>().PlayerCampaignAssignmentId, null, [], null, false)));
        Services.AddSingleton(context);
    }

    // ── Queue truth ────────────────────────────────────────────────────────────

    [Fact]
    public void QueueRendersUnfilteredSectionTotalsIndependentOfTheLoadedPage()
    {
        RegisterServices();

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Chen"));

        var sections = cut.FindAll("button.place-section").Select(Collapse).ToList();
        sections.Count.ShouldBe(4);
        sections[0].ShouldBe("34 Needs placement");
        sections[1].ShouldBe("21 Assigned this season");
        sections[2].ShouldBe("6 Not selected");
        sections[3].ShouldBe("2 Unavailable");
    }

    [Fact]
    public void QueueBrowsesNeedsPlacementAndWidensTheSectionWhenSearching()
    {
        var queries = RegisterServices();

        var cut = RenderPanel();
        cut.WaitForAssertion(() => _ = queries.Received().GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.Eligibility == "NeedsPlacement"),
            Arg.Any<CancellationToken>()));

        // Search spans the whole campaign rather than the open section, so a match sitting in another section
        // is never hidden behind the browsing default.
        cut.Render(parameters => parameters.Add(component => component.State, new CampaignWorkspacePlacementState { Search = "Chen" }));
        cut.WaitForAssertion(() => _ = queries.Received().GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.Search == "Chen" && input.Eligibility == null),
            Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void QueueSendsEveryAppliedDiscoveryControlToTheAuthoritativeRead()
    {
        var queries = RegisterServices();
        var state = new CampaignWorkspacePlacementState
        {
            Search = "114",
            Eligibility = "OptionalReassignment",
            GraduationYears = [2031, 2032],
            TagDefinitionIds = [11, 12],
            Outcome = "assigned",
            TeamId = 21,
            SortBy = "tryoutNumber",
            SortDirection = "desc",
            Page = 3
        };

        var cut = RenderPanel(state: state);
        cut.WaitForAssertion(() => _ = queries.Received().GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.Search == "114"
                && input.Eligibility == "OptionalReassignment"
                && input.GraduationYears!.SequenceEqual(new[] { 2031, 2032 })
                && input.TagDefinitionIds!.SequenceEqual(new[] { 11L, 12L })
                && input.LocalOutcome == "assigned"
                && input.LocalTeamId == 21
                && input.SortBy == "tryoutNumber"
                && input.SortDirection == "desc"
                && input.Page == 3), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void QueueNeverNarrowsToTheLinkedParticipant()
    {
        var queries = RegisterServices();

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Chen"));

        // The linked participant is resolved from the loaded page, so the queue stays the queue rather than
        // collapsing to one row simply because a handoff selected someone.
        _ = queries.Received().GetCampaignEffectivePlacementsAsync(
            Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void QueueRendersWrittenEmptyStateWithAClearFiltersAction()
    {
        RegisterServices(rows: []);

        var cut = RenderPanel(state: new CampaignWorkspacePlacementState { Search = "nobody" });
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No participants match the current filters."));
        cut.FindAll("button").Any(button => string.Equals(Collapse(button), "Clear filters", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Fact]
    public void QueueReportsItsOwnFailureWithRetry()
    {
        RegisterServices(queueProblem: true);

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The placement queue could not be loaded."));
        cut.FindAll("button").Any(button => string.Equals(Collapse(button), "Retry", StringComparison.Ordinal)).ShouldBeTrue();
    }

    [Fact]
    public void QueueRowsAreLinksCarryingWrittenState()
    {
        RegisterServices();

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Chen"));

        var row = cut.Find("a.place-row");
        row.GetAttribute("href").ShouldNotBeNullOrWhiteSpace();
        Collapse(row).ShouldContain("Undecided");
        Collapse(row).ShouldContain("2032");
        Collapse(row).ShouldContain("114");
    }

    [Fact]
    public void FilterChoicesFailureNamesItsOwnRegionAndKeepsTheQueue()
    {
        RegisterServices();

        var cut = RenderPanel(choicesLoadFailed: true);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Couldn't load filter options."));

        cut.Markup.ShouldContain("Chen");
    }

    // ── Selected participant evidence ──────────────────────────────────────────

    [Fact]
    public void SelectionSeparatesTheLocalDecisionFromAnInheritedEffectivePlacement()
    {
        var team = new CampaignParticipantTeamSummaryDto(21, "Elite Silver");
        RegisterServices(rows:
        [
            CreateRow(301) with
            {
                EffectiveTeam = team,
                EffectiveDecision = new PlacementDecisionSource(
                    CreateDecision(205, PlacementOutcome.Assigned, 21), "Spring Cup", team),
                Eligibility = EffectivePlacementEligibility.OptionalReassignment
            }
        ]);

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Effective season placement"));

        cut.Markup.ShouldContain("Tryout 114");
        cut.Markup.ShouldContain("Elite Silver");
        cut.Markup.ShouldContain("Spring Cup");
        cut.Markup.ShouldContain("Assigned this season");
        // A valid inherited assignment permits optional reassignment but is not this campaign's own decision.
        cut.Markup.ShouldContain("No campaign decision");
    }

    [Fact]
    public void SelectionShowsCorrectionEvidenceForAnInvalidLatestAssignmentWithoutInventingATeam()
    {
        RegisterServices(rows:
        [
            CreateRow(301) with
            {
                CorrectionReason = PlacementCorrectionReason.TeamArchived,
                LocalDecision = CreateDecision(301, PlacementOutcome.Assigned, 21),
                LocalTeam = new CampaignParticipantTeamSummaryDto(21, "Retired Gold")
            }
        ]);

        var cut = RenderPanel(selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Needs correction:"));

        cut.Markup.ShouldContain("The saved team is archived.");
        // No historical fallback and no substituted team.
        cut.Markup.ShouldContain("No effective season placement");
    }

    [Fact]
    public void SelectionRendersAppliedTagsAndTheEvaluateReturnAffordance()
    {
        RegisterServices(rows:
        [
            CreateRow(301) with { AppliedTags = [new CampaignParticipantTagSummaryDto(11, "Fast", "#0E7C7B", false)] }
        ]);

        var cut = RenderPanel(selectedParticipantId: 301, evaluationReturnPath: "/campaigns/10?tab=evaluate&evalParticipant=301");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Applied tags"));

        cut.Markup.ShouldContain("Fast");
        cut.FindAll("a").Single(a => string.Equals(a.TextContent, "Return to evaluation", StringComparison.Ordinal))
            .GetAttribute("href").ShouldBe("/campaigns/10?tab=evaluate&evalParticipant=301");
    }

    [Fact]
    public void SelectionWithoutALinkedParticipantInvitesAChoice()
    {
        RegisterServices();

        var cut = RenderPanel();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Select a participant from the queue"));
    }

    // ── Posture and permissions ────────────────────────────────────────────────

    [Fact]
    public void ClosedCampaignUsesTheImmutableLocalReadAndOffersNoDecisionControls()
    {
        var queries = RegisterServices();

        var cut = RenderPanel(status: CampaignStatus.Closed, selectedParticipantId: 301);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Read-only — campaign is closed."));

        cut.FindAll("#place-outcome").ShouldBeEmpty();
        cut.FindAll("#place-team").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("Save placement");
        _ = queries.Received().GetClosedCampaignRosterAsync(
            Arg.Is<GetClosedCampaignRosterInput>(input => input.ParticipantId == null), Arg.Any<CancellationToken>());
        _ = queries.DidNotReceive().GetCampaignEffectivePlacementsAsync(
            Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ClosedCampaignShowsItsCampaignLocalOutcomeTotals()
    {
        RegisterServices();

        var cut = RenderPanel(status: CampaignStatus.Closed);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No campaign decision"));

        var sections = cut.FindAll("button.place-section").Select(Collapse).ToList();
        sections[0].ShouldBe("1 No campaign decision");
        sections[1].ShouldBe("4 Assigned");
        sections[2].ShouldBe("3 Not selected");
        sections[3].ShouldBe("2 Withdrawn");
    }

    [Fact]
    public void MemberWithoutEditCapabilitySeesEvidenceButNoDecisionControls()
    {
        RegisterServices();

        var cut = RenderPanel(selectedParticipantId: 301, canEdit: false);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Effective season placement"));

        cut.FindAll("#place-outcome").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("Save placement");
        cut.Markup.ShouldNotContain("Read-only — campaign is closed.");
    }

    [Fact]
    public void ReadOnlyCampaignStillShowsTheQueueAndEvidenceRegions()
    {
        RegisterServices();

        var cut = RenderPanel(selectedParticipantId: 301, canEdit: false);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Effective season placement"));

        cut.Markup.ShouldContain("Needs placement");
        cut.Markup.ShouldContain("Chen");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string Collapse(AngleSharp.Dom.IElement element)
        => string.Join(' ', element.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static CampaignSavedPlacementDecision CreateDecision(long assignmentId, PlacementOutcome outcome, long? teamId)
        => new(assignmentId, 7, 10, 5, 1, outcome, teamId, DateTimeOffset.UnixEpoch, 42, "Dana Coach", Guid.NewGuid());

    private static CampaignEffectivePlacementItem CreateRow(long assignmentId) => new(
        assignmentId,
        7,
        "Avery",
        "Chen",
        2032,
        114,
        LifecycleStatus.Active,
        Guid.NewGuid(),
        null,
        null,
        null,
        EffectivePlacementEligibility.NeedsPlacement,
        PlacementCorrectionReason.None)
    {
        AppliedTags = []
    };

    /// <summary>
    /// Holds the next whole-campaign queue read open, so a test can observe the panel while a save settles.
    /// </summary>
    /// <param name="queries">The query service whose queue read is held.</param>
    /// <returns>The signal set once the read has been entered, and the action that releases it.</returns>
    private static (Task Entered, Action Release) HoldNextQueueRead(IEffectivePlacementQueryService queries)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queries.GetCampaignEffectivePlacementsAsync(
                Arg.Is<GetCampaignEffectivePlacementsInput>(input => input.ParticipantId == null), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                entered.TrySetResult();
                await release.Task;
                return new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult([CreateRow(301)], 1));
            });
        return (entered.Task, () => release.TrySetResult());
    }

    private IEffectivePlacementQueryService RegisterServices(
        IReadOnlyList<CampaignEffectivePlacementItem>? rows = null,
        bool queueProblem = false,
        int? totalCount = null)
    {
        _mutations = CreateMutationService();
        _teams = CreateTeamService();
        var queries = CreateQueryService(rows ?? [CreateRow(301)], queueProblem, totalCount);

        Services.AddSingleton(queries);
        Services.AddSingleton(_mutations);
        Services.AddSingleton(CreateSummaryService());
        Services.AddSingleton(_teams);
        return queries;
    }

    private static ICampaignPlacementService CreateMutationService()
    {
        var mutations = Substitute.For<ICampaignPlacementService>();
        mutations.UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>())
            .Returns(call => new ServiceResult<PlacementMutationSuccess>(PlacementTestReceipts.Success(call.Arg<UpdateCampaignPlacementInput>(), _replacementToken)));
        return mutations;
    }

    private static ITeamRosterService CreateTeamService()
    {
        var teams = Substitute.For<ITeamRosterService>();
        teams.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<IReadOnlyList<TeamRosterItem>>(new List<TeamRosterItem>
            {
                new() { TeamId = 21, Name = "Elite Silver", GraduationYear = 2032, LifecycleStatus = LifecycleStatus.Active, ActivePlacementCount = 0 }
            }));
        return teams;
    }

    private static ICampaignPlacementQueryService CreateSummaryService()
    {
        var campaignPlacementQueries = Substitute.For<ICampaignPlacementQueryService>();
        campaignPlacementQueries.GetPlacementSummaryAsync(Arg.Any<GetCampaignPlacementSummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignPlacementSummaryDto>(new CampaignPlacementSummaryDto(4, 3, 2, 1, 10)));
        return campaignPlacementQueries;
    }

    private IEffectivePlacementQueryService CreateQueryService(
        IReadOnlyList<CampaignEffectivePlacementItem> effectiveRows,
        bool queueProblem,
        int? totalCount)
    {
        _effectiveRows = effectiveRows;
        var queries = Substitute.For<IEffectivePlacementQueryService>();
        var effectiveResult = queueProblem
            ? new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("boom"))
            : new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult(effectiveRows, totalCount ?? effectiveRows.Count));

        queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<GetCampaignEffectivePlacementsInput>();
                if (input.ParticipantId is not { } linkedId)
                {
                    return effectiveResult;
                }

                // A linked-participant read is bounded to that participant; the queue read is not.
                var linked = effectiveRows.Where(row => row.PlayerCampaignAssignmentId == linkedId).ToList();
                return new ServiceResult<CampaignEffectivePlacementsResult>(CreateEffectiveResult(linked, linked.Count));
            });

        queries.GetClosedCampaignRosterAsync(Arg.Any<GetClosedCampaignRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.Arg<GetClosedCampaignRosterInput>();
                var items = input.ParticipantId is null or 301
                    ? new List<ClosedCampaignRosterItem> { CreateClosedRow() }
                    : [];
                return new ServiceResult<ClosedCampaignRosterResult>(new ClosedCampaignRosterResult(
                    new PlacementCampaignIdentity(10, "Summer Tryouts", CampaignStatus.Closed, new PlacementSeasonIdentity(5, "2026")),
                    new PagedResult<ClosedCampaignRosterItem>(items, 1, 50, items.Count))
                {
                    ParticipantCount = 1
                });
            });

        return queries;
    }

    private static CampaignEffectivePlacementsResult CreateEffectiveResult(
        IReadOnlyList<CampaignEffectivePlacementItem> rows, int totalCount)
        => new(
            new PlacementCampaignIdentity(10, "Summer Tryouts", CampaignStatus.Active, new PlacementSeasonIdentity(5, "2026")),
            new EffectivePlacementCounts(34, 21, 6, 2),
            new PagedResult<CampaignEffectivePlacementItem>(rows, 1, 50, totalCount));

    private static ClosedCampaignRosterItem CreateClosedRow()
        => new(
            301, 7, "Avery", "Chen", 2032, 114,
            new PlacementDecisionSource(CreateDecision(301, PlacementOutcome.NotSelected, null), "Summer Tryouts", null))
        {
            AppliedTags = []
        };

    /// <summary>The replacement token every successful mutation returns.</summary>
    private static readonly Guid _replacementToken = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>The mutation substitute under test.</summary>
    private ICampaignPlacementService _mutations = null!;

    /// <summary>The compatible-team read substitute under test.</summary>
    private ITeamRosterService _teams = null!;

    /// <summary>The effective-placement rows the query substitute serves.</summary>
    private IReadOnlyList<CampaignEffectivePlacementItem> _effectiveRows = [];

    private IRenderedComponent<CampaignPlacePanel> RenderPanel(
        CampaignStatus status = CampaignStatus.Active,
        long? selectedParticipantId = null,
        bool canEdit = true,
        CampaignWorkspacePlacementState? state = null,
        string? evaluationReturnPath = null,
        bool choicesLoadFailed = false,
        Action<CampaignWorkspacePlacementState>? onStateChanged = null,
        Action<string>? onCampaignTeamSearchChanged = null,
        string? owner = null)
        => Render<CampaignPlacePanel>(parameters =>
        {
            parameters.Add(component => component.CampaignId, 10);
            parameters.Add(component => component.CampaignStatus, status);
            parameters.Add(component => component.SelectedParticipantId, selectedParticipantId);
            parameters.Add(component => component.CanEditPlacements, canEdit);
            parameters.Add(component => component.State, state ?? new CampaignWorkspacePlacementState());
            parameters.Add(component => component.EvaluationReturnPath, evaluationReturnPath);
            parameters.Add(component => component.ChoicesLoadFailed, choicesLoadFailed);
            parameters.Add(component => component.GraduationYearChoices, (IReadOnlyList<int>)[2032]);
            parameters.Add(component => component.TagChoices, (IReadOnlyList<TagDefinitionDto>)[]);
            parameters.Add(component => component.CampaignTeamChoices, (IReadOnlyList<TeamRosterItem>)[]);
            if (owner is not null)
            {
                parameters.Add(component => component.Owner, owner);
            }
            if (onStateChanged is not null)
            {
                parameters.Add(component => component.OnStateChanged, (CampaignWorkspacePlacementState next) => onStateChanged(next));
            }

            if (onCampaignTeamSearchChanged is not null)
            {
                parameters.Add(component => component.OnCampaignTeamSearchChanged, (string search) => onCampaignTeamSearchChanged(search));
            }
        });
}
