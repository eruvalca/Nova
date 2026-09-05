using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Features.Campaigns.Pages;
using NSubstitute;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>Verifies Draft readiness and opening recovery through the rendered administrator controls.</summary>
public sealed class CampaignEntryTests : BunitContext
{
    /// <summary>Verifies preparation readiness labels use the correct player and team nouns.</summary>
    /// <param name="count">The readiness count.</param>
    /// <param name="suffix">The expected plural suffix.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1, "")]
    [InlineData(2, "s")]
    public void CampaignEntry_UsesCountAwareReadinessLabels(int count, string suffix)
    {
        Register(new CampaignOpeningReadinessResult(10, count, count, true, [], [], null));
        var cut = Render<CampaignEntry>(parameters => parameters.Add(component => component.CampaignId, 10));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Draft"));
        cut.FindAll("p").Select(element => element.TextContent.Trim()).ShouldContain($"active player{suffix} will enroll");
        cut.FindAll("p").Select(element => element.TextContent.Trim()).ShouldContain($"active team{suffix}");
    }

    /// <summary>Verifies failed detail reads stop showing loading, while a pending retry restores loading and can recover the Draft.</summary>
    /// <param name="transport">Whether the initial detail request throws a transport exception.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CampaignEntry_ShowsDetailFailureWithoutLoading_AndRetries(bool transport)
    {
        var (queries, _) = Register(ReadyWithoutTeams());
        var pending = new TaskCompletionSource<ServiceResult<CampaignDetailResult>>();
        var calls = 0;
        queries.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls > 1 ? pending.Task : transport
                ? Task.FromException<ServiceResult<CampaignDetailResult>>(new HttpRequestException("Offline"))
                : Task.FromResult(new ServiceResult<CampaignDetailResult>(ServiceProblem.ServerError("Detail unavailable"))));
        var cut = RenderReview();
        cut.WaitForAssertion(() => cut.FindAll("button").ShouldContain(button => button.TextContent.Trim() == "Retry"));
        cut.Markup.ShouldNotContain("Loading campaign");
        cut.Markup.ShouldNotContain("Summer Draft");

        var retry = cut.FindAll("button").Single(button => button.TextContent.Trim() == "Retry")
            .ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Loading campaign"));
        await cut.InvokeAsync(() => pending.SetResult(new ServiceResult<CampaignDetailResult>(DraftDetail(10, "Recovered Draft"))));
        await retry;
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Recovered Draft"));
        cut.Markup.ShouldNotContain("Loading campaign");
        cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse();
    }

    /// <summary>Verifies incompatible recovery markers prevent mutation and remain available for corrected retry.</summary>
    /// <param name="key">The typed recovery marker that fails to deserialize.</param>
    /// <param name="unsupported">Whether the failure is unsupported data rather than malformed JSON.</param>
    /// <param name="active">Whether the campaign was already opened before recovery.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("open:10", false, false)]
    [InlineData("open:10", true, false)]
    [InlineData("delete:10", false, false)]
    [InlineData("delete:10", true, false)]
    [InlineData("open:10", false, true)]
    [InlineData("open:10", true, true)]
    public void CampaignEntry_PreservesIncompatibleRecovery_UntilCorrectedRetry(string key, bool unsupported, bool active)
    {
        var corrupt = true;
        var operationId = Guid.NewGuid();
        Exception failure = unsupported ? new NotSupportedException("Unsupported marker") : new System.Text.Json.JsonException("Malformed marker");
        var (queries, lifecycle) = Register(ReadyWithoutTeams(), persistedOpeningId: operationId,
            incompatibleRead: () => corrupt, incompatibleKey: key, incompatibleException: failure);
        if (active)
        {
            queries.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
                .Returns(new ServiceResult<CampaignDetailResult>(DraftDetail(10, "Summer Draft") with { Status = CampaignStatus.Active }));
        }
        lifecycle.OpenAsync(10, Arg.Any<OpenCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<OpenCampaignResult>(ServiceProblem.ServerError("Still uncertain")));
        var cut = RenderReview();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Stored campaign recovery data is incompatible"));
        if (!active)
        {
            cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeTrue();
        }
        _ = lifecycle.DidNotReceive().OpenAsync(Arg.Any<long>(), Arg.Any<OpenCampaignInput>(), Arg.Any<CancellationToken>());
        _ = lifecycle.DidNotReceive().DeleteDraftAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        JSInterop.Invocations.ShouldNotContain(invocation => invocation.Identifier == "remove" || invocation.Identifier == "clear");
        corrupt = false;
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Retry").Click();
        cut.WaitForAssertion(() => lifecycle.Received(1).OpenAsync(10,
            Arg.Is<OpenCampaignInput>(input => input.OperationId == operationId), Arg.Any<CancellationToken>()));
    }

    /// <summary>Verifies startup and notification tasks cannot restore an obsolete authentication scope.</summary>
    /// <param name="startup">Whether the older result belongs to startup authentication.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CampaignEntry_IgnoresOvertakenAuthentication(bool startup)
    {
        Register(ReadyWithoutTeams());
        var older = new TaskCompletionSource<AuthenticationState>();
        var authentication = new DeferredAuthentication(startup ? older.Task : Task.FromResult(DeferredAuthentication.State(42)));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = RenderReview();
        if (!startup)
        {
            cut.WaitForAssertion(() => cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse());
            await cut.InvokeAsync(() => authentication.Publish(older.Task));
        }
        await cut.InvokeAsync(() => authentication.Publish(Task.FromResult(DeferredAuthentication.State(43))));
        cut.WaitForAssertion(() => cut.Instance.SnapshotScope.ShouldBe("101:43:True"));

        await cut.InvokeAsync(() => older.SetResult(DeferredAuthentication.State(42)));

        cut.Instance.SnapshotScope.ShouldBe("101:43:True");
        cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse();
        JSInterop.Invocations.ShouldNotContain(invocation => invocation.Identifier == "clear" && invocation.Arguments.Contains("101:43:True"));
    }

    /// <summary>Verifies authentication finishing after disposal cannot reload campaign data.</summary>
    [Fact]
    public async Task CampaignEntry_IgnoresAuthenticationCompletion_AfterDisposal()
    {
        var (queries, _) = Register(ReadyWithoutTeams());
        var authentication = new DeferredAuthentication(Task.FromResult(DeferredAuthentication.State(42)));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = RenderReview();
        cut.WaitForAssertion(() => cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse());
        var pending = new TaskCompletionSource<AuthenticationState>();
        await cut.InvokeAsync(() => authentication.Publish(pending.Task));
        await cut.Instance.DisposeAsync();
        cut.Dispose();
        await cut.InvokeAsync(() => pending.SetResult(DeferredAuthentication.State(43)));
        _ = queries.Received(1).GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies the deployed page can attach the callbacks exercised by these tests.</summary>
    [Fact]
    public void CampaignEntry_DeclaresInteractiveAutoRenderMode()
    {
        var attribute = typeof(CampaignEntry).GetCustomAttributes(false).OfType<RenderModeAttribute>().Single();

        attribute.Mode.ShouldBeOfType<InteractiveAutoRenderMode>();
    }

    /// <summary>Verifies zero players prevent commitment and expose the correction handoff.</summary>
    [Fact]
    public void CampaignEntry_DisablesOpening_WhenNoActivePlayers()
    {
        Register(new CampaignOpeningReadinessResult(10, 0, 0, false,
            [CampaignOpeningBlocker.NoActivePlayers], [CampaignOpeningWarning.NoActiveTeams], null));

        var cut = RenderReview();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Add an active player before opening"));
        cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll("a").Single(link => link.TextContent == "Go to players")
            .GetAttribute("href").ShouldStartWith("/players?");
    }

    /// <summary>Verifies the team warning remains non-blocking after interactive session attachment.</summary>
    [Fact]
    public void CampaignEntry_AllowsOpening_WhenOnlyTeamsAreMissing()
    {
        Register(ReadyWithoutTeams());

        var cut = RenderReview();

        cut.WaitForAssertion(() => cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse());
        cut.Markup.ShouldContain("Evaluation can begin; add teams before placement");
        cut.Find("button.draft-commit").TextContent.ShouldBe("Open campaign and enroll 3 players");
    }

    /// <summary>Verifies member rendering conceals Draft identity even if a stale service snapshot contains it.</summary>
    [Fact]
    public void CampaignEntry_ConcealsDraftIdentity_ForOrdinaryMember()
    {
        Register(ReadyWithoutTeams(), isAdmin: false);

        var cut = RenderReview();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Campaign not found"));
        cut.Markup.ShouldNotContain("Summer Draft");
        cut.Markup.ShouldNotContain("Roster preview");
    }

    /// <summary>Verifies interactive attachment reuses an authorized prerender snapshot without duplicate reads.</summary>
    [Fact]
    public void CampaignEntry_ReusesPersistedSnapshot_WhenIdentityMatches()
    {
        var (queries, _) = Register(ReadyWithoutTeams());

        var cut = Render<PersistedCampaignEntry>(parameters => parameters.Add(component => component.CampaignId, 10));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Restored Draft"));
        _ = queries.DidNotReceive().GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>());
        _ = queries.DidNotReceive().GetOpeningReadinessAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        _ = queries.DidNotReceive().GetCreationSetupAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies a late request for the previous route cannot replace the newly selected Draft.</summary>
    [Fact]
    public async Task CampaignEntry_IgnoresStaleDetailCompletion_AfterCampaignChanges()
    {
        var (queries, _) = Register(ReadyWithoutTeams());
        var pending = new TaskCompletionSource<ServiceResult<CampaignDetailResult>>();
        queries.GetCampaignDetailAsync(Arg.Is<GetCampaignDetailInput>(input => input.CampaignId == 10), Arg.Any<CancellationToken>())
            .Returns(pending.Task);
        queries.GetCampaignDetailAsync(Arg.Is<GetCampaignDetailInput>(input => input.CampaignId == 11), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignDetailResult>(DraftDetail(11, "New Draft")));
        queries.GetOpeningReadinessAsync(11, Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignOpeningReadinessResult>(ReadyWithoutTeams() with { CampaignId = 11 }));
        var cut = RenderReview();

        cut.Render(parameters => parameters.Add(component => component.CampaignId, 11));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("New Draft"));
        await cut.InvokeAsync(() => pending.SetResult(new ServiceResult<CampaignDetailResult>(DraftDetail(10, "Old Draft"))));

        cut.Markup.ShouldContain("New Draft");
        cut.Markup.ShouldNotContain("Old Draft");
        _ = queries.DidNotReceive().GetOpeningReadinessAsync(10, Arg.Any<CancellationToken>());
    }

    /// <summary>Creates a saved Draft header for recovery scenarios.</summary>
    /// <param name="id">The campaign identifier.</param>
    /// <param name="name">The display name distinguishing each snapshot.</param>
    /// <returns>The Draft header.</returns>
    private static CampaignDetailResult DraftDetail(long id, string name) => new()
    {
        CampaignId = id,
        Name = name,
        Status = CampaignStatus.Draft,
        StartDate = new DateOnly(2026, 6, 1),
        ParticipantCount = 0,
        SeasonId = 5,
        SeasonName = "Summer 2026"
    };

    /// <summary>Verifies a failed pre-commit readiness refresh never dispatches an opening mutation.</summary>
    [Fact]
    public void CampaignEntry_PreventsOpening_WhenFreshReadinessFails()
    {
        var (queries, lifecycle) = Register(ReadyWithoutTeams());
        var cut = RenderReview();
        cut.WaitForAssertion(() => cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse());
        queries.GetOpeningReadinessAsync(10, Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignOpeningReadinessResult>(ServiceProblem.ServerError("Readiness unavailable")));

        cut.Find("button.draft-commit").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Readiness unavailable"));
        _ = lifecycle.DidNotReceive().OpenAsync(Arg.Any<long>(), Arg.Any<OpenCampaignInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies corrected contextual metadata errors do not block the next valid submission.</summary>
    [Fact]
    public void CampaignEntry_ResubmitsMetadata_AfterCorrectingServerValidation()
    {
        Register(ReadyWithoutTeams());
        var metadata = Services.GetRequiredService<ICampaignMetadataService>();
        metadata.UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<UpdateCampaignMetadataResult>(ServiceProblem.Validation(
                new Dictionary<string, string[]> { ["StartDate"] = ["Start date is outside the season."] })));
        var cut = RenderReview();
        cut.WaitForAssertion(() => cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse());
        cut.FindAll("button").Single(button => button.TextContent == "Edit").Click();
        cut.Find("#edit-campaign-start-date").Change("2025-12-01");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => cut.FindAll(".validation-message")
            .ShouldContain(message => message.TextContent == "Start date is outside the season."));

        cut.Find("#edit-campaign-start-date").Change("2026-06-02");
        // Re-supply parent parameters with the same error snapshot before attempting submission.
        cut.Render(parameters => parameters.Add(component => component.CampaignId, 10));
        cut.FindAll(".validation-message").ShouldNotContain(message => message.TextContent == "Start date is outside the season.");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => metadata.Received(1).UpdateAsync(
            Arg.Is<UpdateCampaignMetadataInput>(input => input.StartDate == new DateOnly(2026, 6, 2)), Arg.Any<CancellationToken>()));
        _ = metadata.Received(2).UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies initial and recovery submissions cannot bypass failed operation persistence.</summary>
    [Fact]
    public void CampaignEntry_RetriesStorageBeforeOpening_WithTheSameOperation()
    {
        var storageFails = true;
        var (_, lifecycle) = Register(ReadyWithoutTeams(), failOpeningStorageWrite: () => storageFails);
        lifecycle.OpenAsync(10, Arg.Any<OpenCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(call => new ServiceResult<OpenCampaignResult>(new OpenCampaignResult(
                call.ArgAt<OpenCampaignInput>(1).OperationId, 10, DateTimeOffset.UtcNow, 101, 3, 0,
                [CampaignOpeningWarning.NoActiveTeams])));
        var cut = RenderReview();
        cut.WaitForAssertion(() => cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse());

        cut.Find("button.draft-commit").Click();
        cut.WaitForAssertion(() => cut.Find("button.draft-commit").TextContent.ShouldBe("Confirm opening result"));
        cut.Find("button.draft-commit").Click();
        _ = lifecycle.DidNotReceive().OpenAsync(Arg.Any<long>(), Arg.Any<OpenCampaignInput>(), Arg.Any<CancellationToken>());

        storageFails = false;
        cut.Find("button.draft-commit").Click();
        cut.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/campaigns/10/roster"));
        var attempts = JSInterop.Invocations.Where(invocation => invocation.Identifier == "write"
            && invocation.Arguments.Contains("open:10")).Select(invocation => (string)invocation.Arguments[2]!).ToList();
        attempts.Count.ShouldBe(3);
        attempts.Distinct().Count().ShouldBe(1);
        _ = lifecycle.Received(1).OpenAsync(10,
            Arg.Is<OpenCampaignInput>(input => input.OperationId == Guid.Parse(attempts[0])), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies an uncertain opening retries the original operation and uses the actual receipt count.</summary>
    [Fact]
    public void CampaignEntry_ReusesOpeningOperation_WhenResponseIsAmbiguous()
    {
        var (_, lifecycle) = Register(ReadyWithoutTeams());
        var operations = new List<Guid>();
        lifecycle.OpenAsync(10, Arg.Any<OpenCampaignInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var operation = call.Arg<OpenCampaignInput>().OperationId;
            operations.Add(operation);
            return Task.FromResult(operations.Count == 1
                ? new ServiceResult<OpenCampaignResult>(ServiceProblem.ServerError("Response was lost"))
                : new ServiceResult<OpenCampaignResult>(new OpenCampaignResult(operation, 10,
                    DateTimeOffset.UtcNow, 101, 4, 0, [CampaignOpeningWarning.NoActiveTeams])));
        });
        var cut = RenderReview();
        cut.WaitForAssertion(() => cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse());

        cut.Find("button.draft-commit").Click();
        cut.WaitForAssertion(() => cut.Find("button.draft-commit").TextContent.ShouldBe("Confirm opening result"));
        cut.Find("button.draft-commit").Click();

        cut.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.ShouldContain("/campaigns/10/roster"));
        operations.Count.ShouldBe(2);
        operations[0].ShouldNotBe(Guid.Empty);
        operations[1].ShouldBe(operations[0]);
        var receipt = JSInterop.Invocations.Where(invocation => invocation.Identifier == "write")
            .SelectMany(invocation => invocation.Arguments).OfType<OpenCampaignResult>().Single();
        receipt.EnrolledPlayerCount.ShouldBe(4);
    }

    /// <summary>Verifies reload recovers the saved opening operation even after the campaign became Active.</summary>
    [Fact]
    public void CampaignEntry_ReplaysPersistedOpening_AfterCampaignAlreadyOpened()
    {
        var operationId = Guid.NewGuid();
        var (queries, lifecycle) = Register(ReadyWithoutTeams(), persistedOpeningId: operationId);
        queries.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignDetailResult>(DraftDetail(10, "Opened Draft") with
            { Status = CampaignStatus.Active, ParticipantCount = 12 }));
        lifecycle.OpenAsync(10, Arg.Any<OpenCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<OpenCampaignResult>(new OpenCampaignResult(operationId, 10,
                DateTimeOffset.UtcNow, 101, 4, 0, [CampaignOpeningWarning.NoActiveTeams])));

        var cut = RenderReview();

        cut.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.ShouldContain("/campaigns/10/roster"));
        _ = lifecycle.Received(1).OpenAsync(10, Arg.Is<OpenCampaignInput>(input => input.OperationId == operationId), Arg.Any<CancellationToken>());
        _ = queries.DidNotReceive().GetOpeningReadinessAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        var receipt = JSInterop.Invocations.Where(invocation => invocation.Identifier == "write")
            .SelectMany(invocation => invocation.Arguments).OfType<OpenCampaignResult>().Single();
        receipt.EnrolledPlayerCount.ShouldBe(4);
        JSInterop.Invocations.ShouldContain(invocation => invocation.Identifier == "remove"
            && invocation.Arguments.Contains("open:10"));
    }

    /// <summary>Verifies a competing administrator's opening navigates to current work without inventing a receipt.</summary>
    [Fact]
    public void CampaignEntry_DoesNotClaimOpeningReceipt_WhenAnotherAdministratorOpened()
    {
        var (queries, lifecycle) = Register(ReadyWithoutTeams());
        var cut = RenderReview();
        cut.WaitForAssertion(() => cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse());
        queries.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignDetailResult>(DraftDetail(10, "Opened elsewhere") with { Status = CampaignStatus.Active }));
        lifecycle.OpenAsync(10, Arg.Any<OpenCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<OpenCampaignResult>(ServiceProblem.Conflict("Another administrator opened this campaign.")));

        cut.Find("button.draft-commit").Click();

        cut.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.ShouldContain("/campaigns/10/roster"));
        JSInterop.Invocations.Where(invocation => invocation.Identifier == "write")
            .SelectMany(invocation => invocation.Arguments).OfType<OpenCampaignResult>().ShouldBeEmpty();
        JSInterop.Invocations.ShouldContain(invocation => invocation.Identifier == "remove"
            && invocation.Arguments.Contains("open:10"));
    }

    /// <summary>Verifies loss of campaign visibility clears Draft data even when recovery storage cannot be removed.</summary>
    /// <param name="kind">The authoritative query result revoking access to the Draft.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(ServiceProblemKind.NotFound)]
    [InlineData(ServiceProblemKind.Forbidden)]
    public void CampaignEntry_ConcealsDraft_WhenRecoveryLosesAccessAndStorageRemovalFails(ServiceProblemKind kind)
    {
        var (queries, lifecycle) = Register(ReadyWithoutTeams(), persistedOpeningId: Guid.NewGuid(), failStorageRemoval: true);
        queries.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignDetailResult>(DraftDetail(10, "Sensitive Draft")),
                new ServiceResult<CampaignDetailResult>(new ServiceProblem(kind)));

        var cut = RenderReview();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Campaign not found"));
        cut.Markup.ShouldNotContain("Sensitive Draft");
        cut.Markup.ShouldNotContain("Roster preview");
        cut.Markup.ShouldNotContain("Summer 2026");
        JSInterop.Invocations.ShouldContain(invocation => invocation.Identifier == "remove"
            && invocation.Arguments.Contains("open:10"));
        _ = lifecycle.DidNotReceive().OpenAsync(Arg.Any<long>(), Arg.Any<OpenCampaignInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies reload reconciles a completed Draft deletion through its tombstone despite missing detail.</summary>
    [Fact]
    public void CampaignEntry_ReplaysPendingDeletion_WhenDetailIsAlreadyNotFound()
    {
        var (queries, lifecycle) = Register(ReadyWithoutTeams(), persistedDeletion: true);
        queries.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignDetailResult>(ServiceProblem.NotFound()));
        lifecycle.DeleteDraftAsync(10, Arg.Any<CancellationToken>()).Returns(new ServiceResult<Success>(new Success()));

        var cut = RenderReview();

        cut.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.ShouldContain("/campaigns?deleted=true"));
        _ = lifecycle.Received(1).DeleteDraftAsync(10, Arg.Any<CancellationToken>());
        JSInterop.Invocations.ShouldContain(invocation => invocation.Identifier == "remove" && invocation.Arguments.Contains("delete:10"));
    }

    /// <summary>Verifies Retry restores mutation controls after a transient recovery-storage read failure.</summary>
    [Fact]
    public void CampaignEntry_RetriesStorage_AfterTransientReadFailure()
    {
        var failRead = true;
        Register(ReadyWithoutTeams(), storageReadFails: () => failRead);
        var cut = RenderReview();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Recovery storage is unavailable"));
        cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeTrue();

        failRead = false;
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Retry").Click();

        cut.WaitForAssertion(() => cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse());
        cut.Markup.ShouldNotContain("Recovery storage is unavailable");
    }

    /// <summary>Verifies abandoning an invalid edit clears server field errors before starting a new edit.</summary>
    /// <param name="switchToTeam">Whether the edit is abandoned through team creation instead of Cancel.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void CampaignEntry_ClearsFieldValidation_WhenStartingAnotherEdit(bool switchToTeam)
    {
        Register(ReadyWithoutTeams());
        var metadata = Services.GetRequiredService<ICampaignMetadataService>();
        metadata.UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<UpdateCampaignMetadataResult>(ServiceProblem.Validation(
                new Dictionary<string, string[]> { ["Name"] = ["This name was rejected by the server."] })));
        var cut = RenderReview();
        cut.WaitForAssertion(() => cut.Find("button.draft-commit").HasAttribute("disabled").ShouldBeFalse());
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Edit").Click();
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("This name was rejected by the server."));

        cut.FindAll("button").Single(button => button.TextContent.Trim() == (switchToTeam ? "Create team" : "Cancel")).Click();
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Edit").Click();

        cut.WaitForAssertion(() => cut.Find("#edit-campaign-name").GetAttribute("value").ShouldBe("Summer Draft"));
        cut.Markup.ShouldNotContain("This name was rejected by the server.");
        cut.FindAll(".validation-message").ShouldBeEmpty();
    }

    /// <summary>Renders the URL-backed review board for the seeded Draft.</summary>
    /// <returns>The rendered page.</returns>
    private IRenderedComponent<CampaignEntry> RenderReview()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10?review=open");
        return Render<CampaignEntry>(parameters => parameters.Add(component => component.CampaignId, 10));
    }

    /// <summary>Creates the advisory state allowing evaluation without existing teams.</summary>
    /// <returns>The opening-ready snapshot.</returns>
    private static CampaignOpeningReadinessResult ReadyWithoutTeams()
        => new(10, 3, 0, true, [], [CampaignOpeningWarning.NoActiveTeams], null);

    /// <summary>Registers tenant-scoped service doubles and browser session interop.</summary>
    /// <param name="readiness">The initial readiness snapshot.</param>
    /// <param name="isAdmin">Whether the caller has campaign-management authority.</param>
    /// <param name="persistedOpeningId">The pending opening operation recovered from session storage.</param>
    /// <param name="failStorageRemoval">Whether session removal throws to exercise unavailable browser storage.</param>
    /// <param name="failOpeningStorageWrite">Whether the opening-operation write should fail.</param>
    /// <param name="persistedDeletion">Whether the tab contains a pending deletion.</param>
    /// <param name="storageReadFails">Controls whether the opening-marker read fails.</param>
    /// <param name="incompatibleRead">Controls whether a typed marker is incompatible.</param>
    /// <param name="incompatibleKey">The incompatible marker key.</param>
    /// <param name="incompatibleException">The typed deserialization failure.</param>
    /// <returns>The query and lifecycle doubles available for scenario-specific behavior.</returns>
    private (ICampaignQueryService Queries, ICampaignLifecycleService Lifecycle) Register(CampaignOpeningReadinessResult readiness, bool isAdmin = true, Guid? persistedOpeningId = null, bool failStorageRemoval = false, Func<bool>? failOpeningStorageWrite = null, bool persistedDeletion = false, Func<bool>? storageReadFails = null, Func<bool>? incompatibleRead = null, string? incompatibleKey = null, Exception? incompatibleException = null)
    {
        ComponentFactories.AddStub<CampaignWorkspace>();
        var queries = Substitute.For<ICampaignQueryService>();
        queries.GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignDetailResult>(new CampaignDetailResult
            {
                CampaignId = 10,
                Name = "Summer Draft",
                Status = CampaignStatus.Draft,
                StartDate = new DateOnly(2026, 6, 1),
                ParticipantCount = 0,
                SeasonId = 5,
                SeasonName = "Summer 2026"
            }));
        queries.GetOpeningReadinessAsync(10, Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignOpeningReadinessResult>(readiness));
        queries.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignCreationSetupResult>(new CampaignCreationSetupResult
            {
                CurrentSeason = new CampaignSeasonChoice { SeasonId = 5, Name = "Summer 2026", StartDate = new DateOnly(2026, 1, 1) },
                ActivePlayerCount = readiness.ActivePlayerCount,
                ActiveTeamCount = readiness.ActiveTeamCount
            }));
        var lifecycle = Substitute.For<ICampaignLifecycleService>();
        Services.AddSingleton(queries);
        Services.AddSingleton(lifecycle);
        Services.AddSingleton(Substitute.For<ICampaignMetadataService>());
        Services.AddSingleton(Substitute.For<ITeamManagementService>());
        Services.AddSingleton<AuthenticationStateProvider>(new AdministratorAuthentication(isAdmin));
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule("./_content/Nova.UI/Features/Campaigns/Pages/CampaignEntry.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        if (incompatibleRead is not null)
        {
            if (incompatibleKey == "open:10")
            {
                module.Setup<string?>("read", invocation => incompatibleRead() && invocation.Arguments.Contains(incompatibleKey)).SetException(incompatibleException!);
            }
            else
            {
                module.Setup<bool?>("read", invocation => incompatibleRead() && invocation.Arguments.Contains(incompatibleKey)).SetException(incompatibleException!);
            }
        }
        if (failOpeningStorageWrite is not null)
        {
            module.SetupVoid("write", invocation => invocation.Arguments.Contains("open:10") && failOpeningStorageWrite())
                .SetException(new JSException("Session storage quota exceeded"));
        }
        var hasPendingOpening = persistedOpeningId.HasValue;
        var hasPendingDeletion = persistedDeletion;
        if (storageReadFails is not null)
        {
            module.Setup<string?>("read", invocation => storageReadFails() && invocation.Arguments.Contains("open:10"))
                .SetException(new JSException("Storage temporarily unavailable"));
        }
        if (persistedDeletion)
        {
            module.Setup<bool?>("read", invocation => hasPendingDeletion && invocation.Arguments.Contains("delete:10")).SetResult(true);
        }
        if (persistedOpeningId is { } saved)
        {
            module.Setup<string?>("read", invocation => hasPendingOpening && !(incompatibleKey == "open:10" && incompatibleRead?.Invoke() == true)
                && invocation.Arguments.Contains("open:10")).SetResult(saved.ToString());
        }
        if (persistedOpeningId is not null || persistedDeletion)
        {
            var removal = module.SetupVoid("remove", invocation =>
            {
                if (!failStorageRemoval && invocation.Arguments.Contains("open:10"))
                {
                    hasPendingOpening = false;
                }
                if (!failStorageRemoval && invocation.Arguments.Contains("delete:10"))
                {
                    hasPendingDeletion = false;
                }

                return true;
            });
            if (failStorageRemoval)
            {
                removal.SetException(new JSException("Session storage unavailable"));
            }
            else
            {
                removal.SetVoidResult();
            }
        }
        return (queries, lifecycle);
    }

    /// <summary>Seeds a matching prerender snapshot before normal page initialization.</summary>
    /// <param name="queries">The campaign reads.</param>
    /// <param name="lifecycle">The lifecycle mutations.</param>
    /// <param name="metadata">The metadata mutations.</param>
    /// <param name="teams">The durable-team mutations.</param>
    /// <param name="authentication">The current identity.</param>
    /// <param name="navigation">The test navigation service.</param>
    /// <param name="js">The session interop service.</param>
    private sealed class PersistedCampaignEntry(ICampaignQueryService queries, ICampaignLifecycleService lifecycle,
        ICampaignMetadataService metadata, ITeamManagementService teams, AuthenticationStateProvider authentication,
        NavigationManager navigation, IJSRuntime js)
        : CampaignEntry(queries, lifecycle, metadata, teams, authentication, navigation, js)
    {
        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            Detail = DraftDetail(10, "Restored Draft");
            Readiness = ReadyWithoutTeams();
            Setup = new CampaignCreationSetupResult { ActivePlayerCount = 3, ActiveTeamCount = 0 };
            SnapshotScope = "101:42:True";
            return base.OnInitializedAsync();
        }
    }

    /// <summary>Controls authentication completion independently of notification order.</summary>
    /// <param name="initial">The startup authentication task.</param>
    private sealed class DeferredAuthentication(Task<AuthenticationState> initial) : AuthenticationStateProvider
    {
        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => initial;

        /// <summary>Publishes an authentication task.</summary>
        /// <param name="state">The pending state.</param>
        public void Publish(Task<AuthenticationState> state) => NotifyAuthenticationStateChanged(state);

        /// <summary>Creates an administrator state for the specified club.</summary>
        /// <param name="clubId">The current club.</param>
        /// <returns>The authenticated state.</returns>
        public static AuthenticationState State(long clubId) => new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "101"), new Claim(NovaClaimTypes.ClubId, clubId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Role, Roles.ClubAdmin)], "Test")));
    }

    /// <summary>Provides the administrator identity used to own recovery state.</summary>
    /// <param name="isAdmin">Whether the test identity has administrator authority.</param>
    private sealed class AdministratorAuthentication(bool isAdmin) : AuthenticationStateProvider
    {
        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "101"), new Claim(NovaClaimTypes.ClubId, "42"),
                new Claim(ClaimTypes.Role, isAdmin ? Roles.ClubAdmin : "Member")], "Test"))));
    }
}
