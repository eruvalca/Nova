using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;
using CampaignParticipantDrawerComponent = Nova.UI.Features.Campaigns.Components.CampaignParticipantDrawer;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignParticipantDrawerTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawerRecoversTagChoicesTransportFailureWithoutConcealingEvidence(bool cancellation)
    {
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                notes: [CreateNote(content: "Shared observation")], tags: [CreateTag(tagName: "Applied trait")],
                capabilities: MutationCapabilities(canApplyTag: true)))));
        var choices = Substitute.For<ITagDefinitionQueryService>();
        var failure = cancellation ? (Exception)new OperationCanceledException("Transport timed out") : new HttpRequestException("Connection lost");
        choices.GetChoicesAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromException<ServiceResult<IReadOnlyList<TagDefinitionDto>>>(failure),
            Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(CreateTagChoices().ToList())));
        RegisterServices(query, tagDefinitionQueryService: choices);

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Shared observation"));
        cut.Markup.ShouldContain("Applied trait");
        FindButtonByText(cut, "Retry").Click();
        cut.WaitForAssertion(() => cut.Find("select[aria-label='Tag to apply']").TextContent.ShouldContain("Captain"));
        cut.Markup.ShouldNotContain("could not be loaded");
        _ = choices.Received(2).GetChoicesAsync(Arg.Any<CancellationToken>());
        _ = query.Received(1).GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
        var evidence = Services.GetRequiredService<ICampaignEvaluationQueryService>();
        _ = evidence.Received(1).GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
        _ = evidence.Received(1).GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrawerIgnoresOldTagChoiceFailureAndDoesNotRestartNewEvidenceAsync(bool restored)
    {
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(
                assignmentId: call.Arg<GetCampaignParticipantDetailInput>().PlayerCampaignAssignmentId,
                notes: [CreateNote(content: "Current observation")], capabilities: MutationCapabilities(canApplyTag: true)))));
        var choices = Substitute.For<ITagDefinitionQueryService>();
        var old = new TaskCompletionSource<ServiceResult<IReadOnlyList<TagDefinitionDto>>>();
        choices.GetChoicesAsync(Arg.Any<CancellationToken>()).Returns(old.Task,
            Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(CreateTagChoices().ToList())));
        RegisterServices(query, tagDefinitionQueryService: choices);
        var evidence = Services.GetRequiredService<ICampaignEvaluationQueryService>();
        var currentNotes = new TaskCompletionSource<ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>>();
        evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>()).Returns(currentNotes.Task);
        var cut = Render<EvidenceStartupDrawer>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301)
            .Add(component => component.SeedDetail, restored ? CreateDetail(capabilities: MutationCapabilities(canApplyTag: true)) : null));
        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));
        await cut.WaitForAssertionAsync(() => _ = evidence.Received(1).GetNotesAsync(
            Arg.Is<GetEvaluationHistoryInput>(input => input.PlayerCampaignAssignmentId == 302), Arg.Any<CancellationToken>()));
        try
        {
            await cut.InvokeAsync(() => old.SetException(new HttpRequestException("Obsolete choices")));
            await cut.Instance.StartupCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
            currentNotes.Task.IsCompleted.ShouldBeFalse();
            _ = evidence.Received(1).GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
            _ = evidence.DidNotReceive().GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            currentNotes.TrySetResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(
                new EvaluationHistoryPage<CampaignParticipantNoteDto>([CreateNote(content: "Current observation")], null)));
        }

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Current observation"));
        cut.Find("select[aria-label='Tag to apply']").TextContent.ShouldContain("Lefty");
        cut.Markup.ShouldContain("Current observation");
        cut.FindAll("button").ShouldNotContain(button => string.Equals(button.TextContent.Trim(), "Retry", StringComparison.Ordinal));
        _ = evidence.Received(1).GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
        _ = evidence.Received(1).GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawerRecoversIdentityTransportFailureThroughDetailRetry(bool cancellation)
    {
        var query = Substitute.For<ICampaignParticipantQueryService>();
        var failure = cancellation ? (Exception)new OperationCanceledException("Transport timed out") : new HttpRequestException("Connection lost");
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromException<ServiceResult<CampaignParticipantDetailDto>>(failure),
            Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(notes: [CreateNote(content: "Recovered observation")]))));
        RegisterServices(query);
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));

        cut.Find("#participant-drawer-retry").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Recovered observation"));
        cut.Markup.ShouldContain("Avery Johnson");
        cut.FindAll("#participant-drawer-retry").ShouldBeEmpty();
        _ = query.Received(2).GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DrawerRecoversOnlyFailedEvidenceRegionAfterTransportError(bool applications, bool cancellation)
    {
        RegisterServices();
        var evidence = Services.GetRequiredService<ICampaignEvaluationQueryService>();
        var failure = cancellation ? (Exception)new OperationCanceledException("Transport timed out.") : new HttpRequestException("Connection lost.");
        var notes = new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([CreateNote(content: "Shared note survives")], null));
        var tags = new ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>([CreateTag(tagName: "Shared trait survives")], null));
        evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(applications ? Task.FromResult(notes) : Task.FromException<ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>>(failure), Task.FromResult(notes));
        evidence.GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(applications ? Task.FromException<ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>>(failure) : Task.FromResult(tags), Task.FromResult(tags));

        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain(applications ? "Shared note survives" : "Shared trait survives"));
        cut.Markup.ShouldContain("Avery Johnson");
        FindButtonByText(cut, applications ? "Retry applications" : "Retry notes").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Shared note survives");
            cut.Markup.ShouldContain("Shared trait survives");
            cut.Markup.ShouldNotContain(applications ? "Retry applications" : "Retry notes");
        });
        _ = evidence.Received(applications ? 1 : 2).GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
        _ = evidence.Received(applications ? 2 : 1).GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DrawerIgnoresObsoleteEvidenceFailureAfterParticipantChangesAsync(bool applications)
    {
        RegisterServices();
        var evidence = Services.GetRequiredService<ICampaignEvaluationQueryService>();
        var oldNotes = new TaskCompletionSource<ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>>();
        var oldTags = new TaskCompletionSource<ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>>();
        var notes = new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([CreateNote(content: "Current player note")], null));
        var tags = new ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>([CreateTag(tagName: "Current player trait")], null));
        evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(applications ? Task.FromResult(notes) : oldNotes.Task, Task.FromResult(notes));
        evidence.GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
            .Returns(applications ? oldTags.Task : Task.FromResult(tags), Task.FromResult(tags));
        var cut = Render<CampaignParticipantDrawerComponent>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        cut.Render(parameters => parameters.Add(component => component.ParticipantId, 302));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain(applications ? "Current player trait" : "Current player note"));

        await cut.InvokeAsync(() =>
        {
            if (applications) { oldTags.SetException(new HttpRequestException("Obsolete participant failure")); }
            else { oldNotes.SetException(new HttpRequestException("Obsolete participant failure")); }
        });

        cut.Markup.ShouldContain("Current player note");
        cut.Markup.ShouldContain("Current player trait");
        cut.Markup.ShouldNotContain("Retry notes");
        cut.Markup.ShouldNotContain("Retry applications");
        cut.Markup.ShouldNotContain("Obsolete participant failure");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("identity")]
    [InlineData("notes")]
    [InlineData("applications")]
    [InlineData("choices")]
    public async Task DrawerPropagatesComponentOwnedCancellationFromEachStartupRegionAsync(string region)
    {
        var query = Substitute.For<ICampaignParticipantQueryService>();
        query.GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignParticipantDetailDto>(CreateDetail(capabilities: MutationCapabilities(canApplyTag: true)))));
        RegisterServices(query);
        var entered = new TaskCompletionSource<CancellationToken>();
        DelayStartupRegion(region, entered);
        var cut = Render<EvidenceStartupDrawer>(parameters => parameters
            .Add(component => component.CampaignId, 10).Add(component => component.ParticipantId, 301));
        var requestToken = await entered.Task.WaitAsync(Xunit.TestContext.Current.CancellationToken);
        requestToken.IsCancellationRequested.ShouldBeFalse();

        await ((IAsyncDisposable)cut.Instance).DisposeAsync();

        var propagated = await cut.Instance.StartupCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        propagated.ShouldBe(requestToken);
        propagated.IsCancellationRequested.ShouldBeTrue();
    }

    private void DelayStartupRegion(string region, TaskCompletionSource<CancellationToken> entered)
    {
        var evidence = Services.GetRequiredService<ICampaignEvaluationQueryService>();
        if (string.Equals(region, "identity", StringComparison.Ordinal))
        {
            Services.GetRequiredService<ICampaignParticipantQueryService>().GetParticipantDetailAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>())
                .Returns(call => UntilCanceledAsync<CampaignParticipantDetailDto>(entered, call.Arg<CancellationToken>()));
        }
        else if (string.Equals(region, "notes", StringComparison.Ordinal))
        {
            evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
                .Returns(call => UntilCanceledAsync<EvaluationHistoryPage<CampaignParticipantNoteDto>>(entered, call.Arg<CancellationToken>()));
        }
        else if (string.Equals(region, "applications", StringComparison.Ordinal))
        {
            evidence.GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>())
                .Returns(call => UntilCanceledAsync<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(entered, call.Arg<CancellationToken>()));
        }
        else
        {
            Services.GetRequiredService<ITagDefinitionQueryService>().GetChoicesAsync(Arg.Any<CancellationToken>())
                .Returns(call => UntilCanceledAsync<IReadOnlyList<TagDefinitionDto>>(entered, call.Arg<CancellationToken>()));
        }
    }

    private static async Task<ServiceResult<T>> UntilCanceledAsync<T>(TaskCompletionSource<CancellationToken> entered, CancellationToken token)
    {
        var pending = new TaskCompletionSource<ServiceResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() => pending.TrySetCanceled(token));
        entered.TrySetResult(token);
        return await pending.Task;
    }

#pragma warning disable CA1812 // bUnit constructs the lifecycle-observing test component through reflection.
    private sealed class EvidenceStartupDrawer(
        ICampaignParticipantQueryService participantQueryService,
        ICampaignEvaluationQueryService evaluationQueryService,
        ICampaignEvaluationNoteService noteService,
        ICampaignTagApplicationService tagApplicationService,
        ITagDefinitionQueryService tagDefinitionQueryService,
        IJSRuntime jsRuntime)
        : CampaignParticipantDrawerComponent(participantQueryService, evaluationQueryService, noteService, tagApplicationService, tagDefinitionQueryService, jsRuntime)
#pragma warning restore CA1812
    {
        [Parameter] public CampaignParticipantDetailDto? SeedDetail { get; set; }
        public TaskCompletionSource<CancellationToken> StartupCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StartupCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task OnInitializedAsync()
        {
            if (SeedDetail is not null)
            {
                Initialized = true;
                PersistedDetail = SeedDetail;
                PersistedOwner = $"{AuthorityScope}:{CampaignId}:{ParticipantId}:{AuthorizedStatus}";
            }
            try
            {
                await base.OnInitializedAsync();
            }
            catch (OperationCanceledException exception)
            {
                StartupCancellation.TrySetResult(exception.CancellationToken);
                throw;
            }
            finally
            {
                StartupCompletion.TrySetResult();
            }
        }
    }
}
