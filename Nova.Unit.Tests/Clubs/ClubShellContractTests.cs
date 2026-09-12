using Nova.SharedKernel.Features.Clubs;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

public sealed class ClubShellContractTests
{
    [Fact]
    public void CanonicalRoutesAreStableAndAdministratorRoutesAreRecognized()
    {
        ClubRoutes.Overview.ShouldBe("/club");
        ClubRoutes.Seasons.ShouldBe("/club/seasons");
        ClubRoutes.SeasonDetailTemplate.ShouldBe("/club/seasons/{SeasonId:long}");
        ClubRoutes.SeasonDetail(17).ShouldBe("/club/seasons/17");
        ClubRoutes.StartNextSeason.ShouldBe("/club/seasons/start-next");
        ClubRoutes.Teams.ShouldBe("/club/teams");
        ClubRoutes.TeamDetail(17).ShouldBe("/club/teams/17");
        ClubRoutes.Members.ShouldBe("/club/members");
        ClubRoutes.Requests.ShouldBe("/club/requests");
        ClubRoutes.Tags.ShouldBe("/club/tags");
        ClubRoutes.Crest.ShouldBe("/club/crest");
        // Every approved member reads the seasons directory, so neither it nor its season records
        // are administrator routes; only advancement inside it keeps the demoted-admin recovery.
        ClubRoutes.IsAdministratorRoute("club/seasons").ShouldBeFalse();
        ClubRoutes.IsAdministratorRoute("club/seasons?page=2").ShouldBeFalse();
        ClubRoutes.IsAdministratorRoute("club/seasons/17").ShouldBeFalse();
        ClubRoutes.IsAdministratorRoute("club/seasons/start-next").ShouldBeTrue();
        ClubRoutes.IsAdministratorRoute("club/seasons/start-next?x=1").ShouldBeTrue();
        ClubRoutes.IsAdministratorRoute("club/members").ShouldBeTrue();
        ClubRoutes.IsAdministratorRoute("club/crest").ShouldBeTrue();
        ClubRoutes.IsAdministratorRoute("club/teams/17").ShouldBeFalse();
        // Legacy pre-shell admin route is still linked from the dashboard and uses
        // RequireClubAdmin, so demoted admins recover with the permissions-changed notice there.
        ClubRoutes.IsAdministratorRoute("Clubs/42/admin").ShouldBeTrue();
        ClubRoutes.IsAdministratorRoute("/Clubs/42/admin?tab=requests").ShouldBeTrue();
        ClubRoutes.IsAdministratorRoute("clubs/42").ShouldBeFalse();
        ClubRoutes.IsAdministratorRoute("clubs/onboarding").ShouldBeFalse();
    }

    [Fact]
    public void ClubShellMarkupDeclaresOrderedRoutesMobileCollapseAndNoScriptFallback()
    {
        var root = FindRepoRoot();
        var markup = File.ReadAllText(Path.Join(root, "Nova.UI", "Features", "Clubs", "Components", "ClubShell.razor"));
        var css = File.ReadAllText(Path.Join(root, "Nova.UI", "Features", "Clubs", "Components", "ClubShell.razor.css"));

        var labels = new[] { "Overview", "Seasons", "Teams", "Members", "Requests", "Tags", "Crest" };
        var positions = labels.Select(label => markup.IndexOf($">{label}<", StringComparison.Ordinal)).ToArray();
        positions.ShouldAllBe(position => position >= 0);
        positions.ShouldBeInOrder();
        markup.ShouldContain("@onclick=\"ToggleDirectory\"");
        markup.ShouldContain("aria-expanded=\"@(_isDirectoryOpen ? \"true\" : \"false\")\"");
        css.ShouldContain("@media (scripting: none)");
        css.ShouldContain("min-height: 2.75rem");
    }

    [Fact]
    public void OverviewDeclaresInteractiveAutoAndIndependentRegionRetries()
    {
        var root = FindRepoRoot();
        var markup = File.ReadAllText(Path.Join(root, "Nova.UI", "Features", "Clubs", "Pages", "ClubOverview.razor"));
        var code = File.ReadAllText(Path.Join(root, "Nova.UI", "Features", "Clubs", "Pages", "ClubOverview.razor.cs"));

        markup.ShouldContain("@rendermode InteractiveAuto");
        markup.ShouldContain("RetryIdentityAsync");
        markup.ShouldContain("RetrySeasonAsync");
        markup.ShouldContain("RetryCampaignAsync");
        code.ShouldContain("Task.WhenAll(");
        code.ShouldContain("LoadIdentityAsync(version, requestToken)");
        code.ShouldContain("LoadSeasonAsync(version, requestToken)");
        code.ShouldContain("LoadCampaignAsync(version, requestToken)");
        code.ShouldContain("if (Initialized)");
        code.ShouldContain("RestorePersistedState();");
        code.ShouldContain("item => item.IsCurrent");
        code.ShouldContain("Status = \"active\", Limit = 1");
    }

    /// <summary>Verifies new Club route and recovery components keep logic in required code-behind files.</summary>
    /// <param name="relativePath">The repository-relative Razor file path.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("Nova.UI/Features/Clubs/Components/RegionFailure.razor")]
    [InlineData("Nova.UI/Features/Clubs/Pages/ClubReservedSection.razor")]
    [InlineData("Nova.UI/Features/Clubs/Pages/ClubDetail.razor")]
    [InlineData("Nova.UI/Features/Teams/Pages/LegacyTeamsRedirect.razor")]
    [InlineData("Nova.UI/Features/Seasons/Pages/SeasonDetailReserved.razor")]
    [InlineData("Nova.UI/Features/Seasons/Pages/StartNextSeasonReserved.razor")]
    public void ClubRouteComponentsKeepLogicInCodeBehind(string relativePath)
    {
        var root = FindRepoRoot();
        var razorPath = Path.Join(root, relativePath);
        var markup = File.ReadAllText(razorPath);

        markup.ShouldNotContain("@code");
        markup.ShouldNotContain("@inject");
        File.Exists($"{razorPath}.cs").ShouldBeTrue();
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Nova.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
