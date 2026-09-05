using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Features.Campaigns.Components;
using Nova.UI.Features.Campaigns.Pages;
using Nova.Unit.Tests.Components;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>Verifies tab-scoped creation input and exact pending requests survive reload only for their owner.</summary>
public sealed class NewCampaignRecoveryTests : BunitContext
{
    /// <summary>Verifies incompatible typed recovery data stays intact and retry restores the original operation.</summary>
    /// <param name="key">The stored payload that fails deserialization.</param>
    /// <param name="unsupported">Whether interop rejects the payload type instead of its JSON.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("create-form", false)]
    [InlineData("create-form", true)]
    [InlineData("create-pending", false)]
    [InlineData("create-pending", true)]
    public void NewCampaign_PreservesIncompatibleRecovery_UntilCorrectedRetry(string key, bool unsupported)
    {
        var (module, _, creation) = Register();
        var form = SavedForm();
        var corrupt = true;
        Exception failure = unsupported ? new NotSupportedException("Unsupported payload") : new System.Text.Json.JsonException("Malformed payload");
        if (key == "create-form")
        {
            module.Setup<CampaignCreateFormState?>("read", invocation => corrupt && invocation.Arguments.Contains(key)).SetException(failure);
        }
        else
        {
            module.Setup<CreateCampaignInput?>("read", invocation => corrupt && invocation.Arguments.Contains(key)).SetException(failure);
        }
        module.Setup<CampaignCreateFormState?>("read", invocation => (!corrupt || key != "create-form") && invocation.Arguments.Contains("create-form")).SetResult(form);
        module.Setup<CreateCampaignInput?>("read", invocation => !corrupt && invocation.Arguments.Contains("create-pending")).SetResult(form.ToCreateInput());
        creation.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CreateCampaignResult>(ServiceProblem.ServerError("Uncertain response")));
        var cut = Render<NewCampaign>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Stored creation recovery data is incompatible"));
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        _ = creation.DidNotReceive().CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>());
        module.Invocations.ShouldNotContain(invocation => invocation.Identifier == "clear" || invocation.Identifier == "remove");
        corrupt = false;
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Retry").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Confirm creation result"));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Confirm creation result").Click();
        cut.WaitForAssertion(() => creation.Received(1).CreateAsync(Arg.Is<CreateCampaignInput>(input => input.OperationId == form.OperationId), Arg.Any<CancellationToken>()));
    }

    /// <summary>Verifies startup and notification authentication tasks cannot restore an obsolete identity.</summary>
    /// <param name="startup">Whether the older task is the initial authentication read.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewCampaign_IgnoresOvertakenAuthentication(bool startup)
    {
        Register();
        var older = new TaskCompletionSource<AuthenticationState>();
        var authentication = new ControlledAuthenticationStateProvider(startup ? older.Task : Task.FromResult(AdministratorState(42)));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<NewCampaign>();
        if (!startup)
        {
            cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
            await cut.InvokeAsync(() => authentication.Publish(older.Task));
        }
        await cut.InvokeAsync(() => authentication.Publish(Task.FromResult(AdministratorState(43))));
        cut.WaitForAssertion(() => cut.Instance.SnapshotScope.ShouldBe("101:43:True"));

        await cut.InvokeAsync(() => older.SetResult(AdministratorState(42)));

        cut.Instance.SnapshotScope.ShouldBe("101:43:True");
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        JSInterop.Invocations.ShouldNotContain(invocation => invocation.Identifier == "clear" && invocation.Arguments.Contains("101:43:True"));
    }

    /// <summary>Verifies a pending refresh and its unchanged result retain unsaved input without clearing recovery or reloading setup.</summary>
    [Fact]
    public async Task NewCampaign_PreservesEdits_DuringPendingAndUnchangedAuthentication()
    {
        var (_, authentication, _) = Register();
        var cut = Render<NewCampaign>();
        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#campaign-name").Change("Unsaved current Draft");
        var pending = new TaskCompletionSource<AuthenticationState>();

        await cut.InvokeAsync(() => authentication.Publish(pending.Task));

        cut.Find("#campaign-name").GetAttribute("value").ShouldBe("Unsaved current Draft");
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        await cut.InvokeAsync(() => pending.SetResult(AdministratorState(42)));
        cut.Find("#campaign-name").GetAttribute("value").ShouldBe("Unsaved current Draft");
        cut.Instance.SnapshotScope.ShouldBe("101:42:True");
        _ = Services.GetRequiredService<ICampaignQueryService>().Received(1).GetCreationSetupAsync(Arg.Any<CancellationToken>());
        JSInterop.Invocations.ShouldNotContain(invocation => invocation.Identifier == "clear");
    }

    /// <summary>Verifies pending authentication permits the current creation result and unchanged authentication retains its exact retry payload.</summary>
    [Fact]
    public async Task NewCampaign_PreservesPendingCreation_DuringUnchangedAuthenticationRefresh()
    {
        var (module, authentication, creation) = Register();
        var form = SavedForm();
        module.Setup<CampaignCreateFormState?>("read", invocation => invocation.Arguments.Contains("create-form")).SetResult(form);
        var result = new TaskCompletionSource<ServiceResult<CreateCampaignResult>>();
        creation.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(result.Task, Task.FromResult(new ServiceResult<CreateCampaignResult>(ServiceProblem.ServerError("Still uncertain"))));
        var cut = Render<NewCampaign>();
        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        var create = cut.Find("button[type='submit']").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.WaitForAssertion(() => creation.Received(1).CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>()));
        var authenticationResult = new TaskCompletionSource<AuthenticationState>();
        await cut.InvokeAsync(() => authentication.Publish(authenticationResult.Task));

        await cut.InvokeAsync(() => result.SetResult(new ServiceResult<CreateCampaignResult>(ServiceProblem.ServerError("Current creation is uncertain"))));
        await create;

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Current creation is uncertain"));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Confirm creation result").HasAttribute("disabled").ShouldBeFalse();
        await cut.InvokeAsync(() => authenticationResult.SetResult(AdministratorState(42)));
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Confirm creation result").Click();
        cut.WaitForAssertion(() => creation.Received(2).CreateAsync(
            Arg.Is<CreateCampaignInput>(input => input.OperationId == form.OperationId && input.Name == form.Name
                && input.StartDate == form.StartDate && input.PlannedEndDate == form.PlannedEndDate && input.ExistingSeasonId == form.ExistingSeasonId),
            Arg.Any<CancellationToken>()));
        JSInterop.Invocations.ShouldNotContain(invocation => invocation.Identifier == "clear" || invocation.Identifier == "remove");
    }

    /// <summary>Verifies a pending authentication notification cannot reload setup after disposal.</summary>
    [Fact]
    public async Task NewCampaign_IgnoresAuthenticationCompletion_AfterDisposal()
    {
        Register();
        var authentication = new ControlledAuthenticationStateProvider(Task.FromResult(AdministratorState(42)));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<NewCampaign>();
        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        var pending = new TaskCompletionSource<AuthenticationState>();
        await cut.InvokeAsync(() => authentication.Publish(pending.Task));
        await cut.Instance.DisposeAsync();
        cut.Dispose();
        await cut.InvokeAsync(() => pending.SetResult(AdministratorState(43)));
        _ = Services.GetRequiredService<ICampaignQueryService>().Received(1).GetCreationSetupAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies restored input remains editable and is removed immediately on a same-role club change.</summary>
    /// <param name="cleanupFails">Whether old-club browser cleanup is unavailable.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewCampaign_RestoresInput_AndInvalidatesItWhenClubChanges(bool cleanupFails)
    {
        var (module, authentication, _) = Register();
        if (cleanupFails)
        {
            module.SetupVoid("clear", _ => true).SetException(new JSException("Storage unavailable"));
        }
        module.Setup<CampaignCreateFormState?>("read", invocation => Equals(invocation.Arguments[0], "101:42:True")
                && Equals(invocation.Arguments[1], "create-form"))
            .SetResult(SavedForm());
        var cut = Render<NewCampaign>();

        cut.WaitForAssertion(() => cut.Find("#campaign-name").GetAttribute("value").ShouldBe("Recovered Draft"));
        cut.FindAll("a").Single(link => link.TextContent.Contains("Back to campaigns", StringComparison.OrdinalIgnoreCase))
            .GetAttribute("href").ShouldBe("/campaigns");
        cut.Find("#campaign-start-date").GetAttribute("value").ShouldBe("2026-06-15");
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();

        await cut.InvokeAsync(() => authentication.Publish(Task.FromResult(AdministratorState(43))));

        cut.WaitForAssertion(() => cut.Find("#campaign-name").GetAttribute("value").ShouldBe(string.Empty));
        JSInterop.Invocations.ShouldContain(invocation => invocation.Identifier == "clear"
            && invocation.Arguments.Contains("101:42:True"));
        JSInterop.Invocations.ShouldContain(invocation => invocation.Identifier == "read"
            && invocation.Arguments.Contains("101:43:True"));
    }

    /// <summary>Verifies reloaded uncertain creation retries the original payload and operation identifier.</summary>
    [Fact]
    public void NewCampaign_ReplaysPersistedRequest_BeforeAllowingAnotherSubmission()
    {
        var (module, _, creation) = Register();
        var form = SavedForm();
        var pending = form.ToCreateInput();
        module.Setup<CampaignCreateFormState?>("read", invocation => Equals(invocation.Arguments[1], "create-form"))
            .SetResult(form);
        module.Setup<CreateCampaignInput?>("read", invocation => Equals(invocation.Arguments[1], "create-pending"))
            .SetResult(pending);
        creation.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CreateCampaignResult>(ServiceProblem.ServerError("Still uncertain")));
        var cut = Render<NewCampaign>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Confirm creation result"));
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll("button").Single(button => button.TextContent == "Confirm creation result").Click();

        cut.WaitForAssertion(() => creation.Received(1).CreateAsync(
            Arg.Is<CreateCampaignInput>(input => input.OperationId == pending.OperationId
                && input.Name == pending.Name && input.StartDate == pending.StartDate
                && input.ExistingSeasonId == pending.ExistingSeasonId), Arg.Any<CancellationToken>()));
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.Markup.ShouldContain("Still uncertain");
    }

    /// <summary>Verifies the visible Retry action retries browser recovery storage after a transient read failure.</summary>
    [Fact]
    public void NewCampaign_RetriesSessionStorage_AfterReadFailure()
    {
        var (module, _, _) = Register();
        var readFails = true;
        module.Setup<CampaignCreateFormState?>("read", _ => readFails)
            .SetException(new JSException("Storage unavailable"));
        var cut = Render<NewCampaign>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Recovery storage is unavailable"));
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();

        readFails = false;
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Retry").Click();

        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        cut.Markup.ShouldNotContain("Recovery storage is unavailable");
    }

    /// <summary>Verifies recovery makes the exact request durable again before contacting the server.</summary>
    [Fact]
    public void NewCampaign_RepersistsRequest_BeforeRetryingFailedStorageWrite()
    {
        var (module, _, creation) = Register();
        module.Setup<CampaignCreateFormState?>("read", invocation => invocation.Arguments.Contains("create-form"))
            .SetResult(SavedForm());
        var writeFails = true;
        module.SetupVoid("write", invocation => writeFails && invocation.Arguments.Contains("create-pending"))
            .SetException(new JSException("Storage temporarily unavailable"));
        creation.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            JSInterop.Invocations.Count(invocation => invocation.Identifier == "write" && invocation.Arguments.Contains("create-form"))
                .ShouldBeGreaterThanOrEqualTo(2);
            JSInterop.Invocations.Count(invocation => invocation.Identifier == "write" && invocation.Arguments.Contains("create-pending"))
                .ShouldBe(2);
            return Task.FromResult(new ServiceResult<CreateCampaignResult>(ServiceProblem.ServerError("Still uncertain")));
        });
        var cut = Render<NewCampaign>();
        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Confirm creation result"));
        _ = creation.DidNotReceive().CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>());

        writeFails = false;
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Confirm creation result").Click();

        cut.WaitForAssertion(() => creation.Received(1).CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>()));
    }

    /// <summary>Verifies failed success cleanup leaves the pending request recoverable until saved input is removed.</summary>
    [Fact]
    public void NewCampaign_RetainsPendingRequest_WhenSuccessfulFormCleanupFails()
    {
        var (module, _, creation) = Register();
        var form = SavedForm();
        module.Setup<CampaignCreateFormState?>("read", invocation => invocation.Arguments.Contains("create-form")).SetResult(form);
        module.SetupVoid("remove", invocation => invocation.Arguments.Contains("create-form"))
            .SetException(new JSException("Storage unavailable"));
        creation.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CreateCampaignResult>(new CreateCampaignResult(form.OperationId, 10,
                form.Name, form.StartDate, form.PlannedEndDate, CampaignStatus.Draft,
                5, "Current", new DateOnly(2026, 1, 1), null, false)));
        var cut = Render<NewCampaign>();
        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());

        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Confirm creation result"));
        JSInterop.Invocations.ShouldContain(invocation => invocation.Identifier == "remove" && invocation.Arguments.Contains("create-form"));
        JSInterop.Invocations.ShouldNotContain(invocation => invocation.Identifier == "remove" && invocation.Arguments.Contains("create-pending"));
    }

    /// <summary>Verifies failed input persistence can be retried without reverting edits or changing the logical operation.</summary>
    [Fact]
    public void NewCampaign_RetryRecoveryStorage_PreservesCurrentEditsUntilWriteSucceeds()
    {
        var (module, _, creation) = Register();
        var saved = SavedForm();
        module.Setup<CampaignCreateFormState?>("read", invocation => invocation.Arguments.Contains("create-form")).SetResult(saved);
        var failWrite = true;
        module.SetupVoid("write", invocation => failWrite && invocation.Arguments.Contains("create-form"))
            .SetException(new JSException("Storage unavailable"));
        creation.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CreateCampaignResult>(ServiceProblem.ServerError("Uncertain response")));
        var cut = Render<NewCampaign>();
        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());

        cut.Find("#campaign-name").Change("Current unsaved edit");
        cut.Find("section.campaign-create-board").TriggerEvent("onchange", new Microsoft.AspNetCore.Components.ChangeEventArgs());

        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue());
        var retry = cut.FindAll("button").Single(button => button.TextContent.Trim() == "Retry recovery storage");
        retry.Closest("fieldset").ShouldBeNull();
        retry.Click();
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        module.Invocations.Count(invocation => invocation.Identifier == "write" && invocation.Arguments.Contains("create-form")).ShouldBe(2);

        failWrite = false;
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Retry recovery storage").Click();

        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#campaign-name").GetAttribute("value").ShouldBe("Current unsaved edit");
        cut.Find("#campaign-start-date").GetAttribute("value").ShouldBe("2026-06-15");
        module.Invocations.Count(invocation => invocation.Identifier == "read" && invocation.Arguments.Contains("create-form")).ShouldBe(1);
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => creation.Received(1).CreateAsync(
            Arg.Is<CreateCampaignInput>(input => input.Name == "Current unsaved edit" && input.OperationId == saved.OperationId), Arg.Any<CancellationToken>()));
    }

    /// <summary>Verifies changing club invalidates persisted and form errors before new setup finishes loading.</summary>
    [Fact]
    public async Task NewCampaign_ClearsErrorsAndSnapshot_BeforeNewClubSetupCompletes()
    {
        var (module, authentication, creation) = Register();
        module.Setup<CampaignCreateFormState?>("read", invocation => invocation.Arguments.Contains("101:42:True")
            && invocation.Arguments.Contains("create-form")).SetResult(SavedForm());
        creation.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CreateCampaignResult>(ServiceProblem.Validation(
                new Dictionary<string, string[]> { ["Name"] = ["Old club name error"] })));
        var cut = Render<NewCampaign>();
        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Old club name error"));
        cut.Instance.PersistedPageError = "Old club setup error";
        var pending = new TaskCompletionSource<ServiceResult<CampaignCreationSetupResult>>();
        Services.GetRequiredService<ICampaignQueryService>().GetCreationSetupAsync(Arg.Any<CancellationToken>()).Returns(pending.Task);

        await cut.InvokeAsync(() => authentication.Publish(Task.FromResult(AdministratorState(43))));

        cut.Instance.PersistedPageError.ShouldBeNull();
        cut.Instance.SnapshotScope.ShouldBeNull();
        cut.Markup.ShouldNotContain("Old club name error");
        await cut.InvokeAsync(() => pending.SetResult(new ServiceResult<CampaignCreationSetupResult>(new CampaignCreationSetupResult
        {
            CurrentSeason = new CampaignSeasonChoice { SeasonId = 6, Name = "New club season", StartDate = new DateOnly(2026, 1, 1) },
            ActivePlayerCount = 1,
            ActiveTeamCount = 0
        })));
        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        cut.Markup.ShouldNotContain("Old club name error");
        cut.Markup.ShouldNotContain("Old club setup error");
        cut.FindComponent<CampaignCreateForm>().Instance.ServerErrors.ShouldBeNull();
        cut.FindComponent<CampaignCreateForm>().Instance.ErrorMessage.ShouldBeNull();
        cut.Find("#campaign-name").GetAttribute("value").ShouldBe(string.Empty);
        cut.Find("#campaign-name").Change("New club Draft");
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => creation.Received().CreateAsync(
            Arg.Is<CreateCampaignInput>(input => input.Name == "New club Draft" && input.ExistingSeasonId == 6), Arg.Any<CancellationToken>()));
    }

    /// <summary>Verifies a failed input write from the previous club cannot disable the newly attached form.</summary>
    [Fact]
    public async Task NewCampaign_IgnoresLateInputStorageFailure_AfterClubChanges()
    {
        var (module, authentication, _) = Register();
        var pendingWrite = module.SetupVoid("write", invocation => invocation.Arguments.Contains("101:42:True")
            && invocation.Arguments.Contains("create-form"));
        var cut = Render<NewCampaign>();
        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#campaign-name").Change("Old club edit");
        var change = cut.Find("section.campaign-create-board").TriggerEventAsync("onchange", new Microsoft.AspNetCore.Components.ChangeEventArgs());
        cut.WaitForAssertion(() => pendingWrite.Invocations.Count.ShouldBe(1));

        await cut.InvokeAsync(() => authentication.Publish(Task.FromResult(AdministratorState(43))));
        cut.WaitForAssertion(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        pendingWrite.SetException(new JSException("Old storage failed late"));
        await change;

        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("#campaign-name").GetAttribute("value").ShouldBe(string.Empty);
        cut.Markup.ShouldNotContain("Retry recovery storage");
        cut.FindComponent<CampaignCreateForm>().Instance.ErrorMessage.ShouldBeNull();
    }

    /// <summary>Builds distinctive saved input to detect replacement by newly initialized defaults.</summary>
    /// <returns>The session-restored form.</returns>
    private static CampaignCreateFormState SavedForm() => new()
    {
        OperationId = Guid.NewGuid(),
        Name = "Recovered Draft",
        StartDate = new DateOnly(2026, 6, 15),
        PlannedEndDate = new DateOnly(2026, 6, 20),
        ExistingSeasonId = 5
    };

    /// <summary>Registers successful setup, a mutable administrator identity, and session interop.</summary>
    /// <returns>The session module, identity provider, and mutation double.</returns>
    private (BunitJSModuleInterop Module, ControlledAuthenticationStateProvider Authentication, ICampaignCreationService Creation) Register()
    {
        var queries = Substitute.For<ICampaignQueryService>();
        queries.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignCreationSetupResult>(new CampaignCreationSetupResult
            {
                CurrentSeason = new CampaignSeasonChoice { SeasonId = 5, Name = "Current", StartDate = new DateOnly(2026, 1, 1) },
                ActivePlayerCount = 3,
                ActiveTeamCount = 0
            }));
        var creation = Substitute.For<ICampaignCreationService>();
        var authentication = new ControlledAuthenticationStateProvider(Task.FromResult(AdministratorState(42)));
        Services.AddSingleton(queries);
        Services.AddSingleton(creation);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule("./_content/Nova.UI/Features/Campaigns/Pages/NewCampaign.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        return (module, authentication, creation);
    }

    /// <summary>Creates the administrator identity used by creation recovery scenarios.</summary>
    /// <param name="clubId">The current club claim.</param>
    /// <returns>The authenticated state.</returns>
    private static AuthenticationState AdministratorState(long clubId) => new(new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "101"), new Claim(NovaClaimTypes.ClubId, clubId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Role, Roles.ClubAdmin)], "Test")));

}
