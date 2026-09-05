using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
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
    [Fact]
    public async Task NewCampaign_RestoresInput_AndInvalidatesItWhenClubChanges()
    {
        var (module, authentication, _) = Register();
        module.Setup<CampaignCreateFormState?>("read", invocation => Equals(invocation.Arguments[0], "101:42:True")
                && Equals(invocation.Arguments[1], "create-form"))
            .SetResult(SavedForm());
        var cut = Render<NewCampaign>();

        cut.WaitForAssertion(() => cut.Find("#campaign-name").GetAttribute("value").ShouldBe("Recovered Draft"));
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
