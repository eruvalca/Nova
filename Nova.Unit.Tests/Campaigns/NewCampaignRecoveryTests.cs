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
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>Verifies tab-scoped creation input and exact pending requests survive reload only for their owner.</summary>
public sealed class NewCampaignRecoveryTests : BunitContext
{
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

        await cut.InvokeAsync(() => authentication.ChangeClub(43));

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
    private (BunitJSModuleInterop Module, ChangingAuthentication Authentication, ICampaignCreationService Creation) Register()
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
        var authentication = new ChangingAuthentication();
        Services.AddSingleton(queries);
        Services.AddSingleton(creation);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule("./_content/Nova.UI/Features/Campaigns/Pages/NewCampaign.razor.js");
        module.Mode = JSRuntimeMode.Loose;
        return (module, authentication, creation);
    }

    /// <summary>Publishes same-role club changes without replacing the user's identity.</summary>
    private sealed class ChangingAuthentication : AuthenticationStateProvider
    {
        /// <summary>Stores the current club claim.</summary>
        private long _clubId = 42;

        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "101"), new Claim(NovaClaimTypes.ClubId, _clubId.ToString()),
                new Claim(ClaimTypes.Role, Roles.ClubAdmin)], "Test"))));

        /// <summary>Notifies mounted components that their old club-owned state is no longer authorized.</summary>
        /// <param name="clubId">The new current club.</param>
        public void ChangeClub(long clubId)
        {
            _clubId = clubId;
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
    }
}
