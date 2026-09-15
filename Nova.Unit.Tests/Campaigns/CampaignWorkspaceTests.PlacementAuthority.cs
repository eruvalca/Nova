using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.UI.Features.Campaigns.Components;
using NSubstitute;
using Shouldly;
using CampaignWorkspacePage = Nova.UI.Features.Campaigns.Pages.CampaignWorkspace;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignWorkspaceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationAuthorityLossImmediatelyUnmountsPlacementConfirmationAsync(bool losesClub)
    {
        var authentication = new ChangingPlacementAuthenticationStateProvider(CreatePrincipal(isClubAdmin: true));
        RegisterServices(isClubAdmin: true, authenticationStateProvider: authentication);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10?tab=place&placementParticipant=301");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => cut.Find("#place-outcome").HasAttribute("disabled").ShouldBeFalse());
        await cut.Find("#place-outcome").ChangeAsync(new ChangeEventArgs { Value = "Withdrawn" });
        await cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Save placement", StringComparison.Ordinal)).ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Confirm change"));
        var pending = new TaskCompletionSource<ServiceResult<CampaignDetailResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Services.GetRequiredService<ICampaignQueryService>()
            .GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var remainingClaims = CreatePrincipal(isClubAdmin: true).Claims.Where(claim =>
            losesClub ? !string.Equals(claim.Type, NovaClaimTypes.ClubId, StringComparison.Ordinal) : !string.Equals(claim.Type, ClaimTypes.Role, StringComparison.Ordinal));

        await cut.InvokeAsync(() => authentication.Change(new ClaimsPrincipal(new ClaimsIdentity(remainingClaims, "Test"))));

        await cut.WaitForAssertionAsync(() => cut.FindComponents<CampaignPlacePanel>().ShouldBeEmpty());
        cut.Markup.ShouldNotContain("Confirm change");
        _ = Services.GetRequiredService<ICampaignPlacementService>().DidNotReceive()
            .UpdatePlacementAsync(Arg.Any<UpdateCampaignPlacementInput>(), Arg.Any<CancellationToken>());
    }

    private sealed class ChangingPlacementAuthenticationStateProvider(ClaimsPrincipal initial) : AuthenticationStateProvider
    {
        private ClaimsPrincipal _principal = initial;
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_principal));
        public void Change(ClaimsPrincipal principal)
        {
            _principal = principal;
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
    }
}
