using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Nova.UI.Features.Teams.Pages;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

public sealed class LegacyTeamsRedirectComponentTests : BunitContext
{
    [Fact]
    public void Redirect_PreservesQueryStringForTeamsList()
    {
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("https://localhost/teams?view=archived&search=Blue&graduationYear=2032");

        Render<LegacyTeamsRedirect>();

        navigationManager.Uri.ShouldContain("/club/teams?view=archived&search=Blue&graduationYear=2032");
    }

    [Fact]
    public void Redirect_PreservesQueryStringForTeamDetail()
    {
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("https://localhost/teams/17?returnUrl=%2Fteams%3Fview%3Darchived");

        Render<LegacyTeamsRedirect>(parameters => parameters.Add(p => p.TeamId, 17L));

        navigationManager.Uri.ShouldContain("/club/teams/17?returnUrl=%2Fteams%3Fview%3Darchived");
    }

    [Fact]
    public void Redirect_DoesNotCarryFragmentIntoDestination()
    {
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("https://localhost/teams?view=archived#members");

        Render<LegacyTeamsRedirect>();

        navigationManager.Uri.ShouldContain("/club/teams?view=archived");
        navigationManager.Uri.ShouldNotContain("#members");
    }
}
