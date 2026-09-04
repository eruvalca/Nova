using System.Security.Claims;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Features.Clubs.Pages;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

public sealed class ClubOverviewComponentTests : BunitContext
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_ShowsIdentityCurrentSeasonAndActiveCampaign_ForMemberAndAdministrator(bool isAdministrator)
    {
        var services = Configure(isAdministrator);

        var cut = RenderOverview();

        cut.Markup.ShouldContain("North Star Volleyball Club");
        cut.Markup.ShouldContain("Duluth, MN");
        cut.Markup.ShouldContain("aria-label=\"No club crest\"");
        cut.Markup.ShouldContain("2026–27");
        cut.Markup.ShouldContain("Sep 1, 2026 – May 31, 2027");
        cut.Markup.ShouldContain("Fall evaluations");
        cut.Markup.ShouldContain("Open campaign");
        cut.Markup.ShouldNotContain("Participant");
        if (isAdministrator)
        {
            cut.Markup.ShouldContain("href=\"/club/seasons\"");
            cut.Markup.ShouldContain(">Crest<");
        }
        else
        {
            cut.Markup.ShouldNotContain("href=\"/club/seasons\"");
            cut.Markup.ShouldNotContain(">Crest<");
        }
        services.Identity.Received(1).GetCurrentAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Render_PreservesEverySuccessfulRegion_WhenAnyCombinationFails(
        bool identityFails,
        bool seasonFails,
        bool campaignFails)
    {
        var identity = Substitute.For<IClubIdentityQueryService>();
        identity.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(
            identityFails
                ? new ServiceResult<ClubIdentityResult>(ServiceProblem.ServerError("Identity failed."))
                : new ServiceResult<ClubIdentityResult>(Identity())));
        Configure(
            isAdministrator: false,
            identity,
            seasonFails
                ? new ServiceResult<SeasonPageResult>(ServiceProblem.ServerError("Season failed."))
                : CurrentSeason(),
            campaignFails
                ? new ServiceResult<CampaignListResult>(ServiceProblem.ServerError("Campaign failed."))
                : ActiveCampaigns());

        var cut = RenderOverview();

        cut.Markup.ShouldContain(identityFails ? "Identity failed." : "North Star Volleyball Club");
        cut.Markup.ShouldContain(seasonFails ? "Season failed." : "2026–27");
        cut.Markup.ShouldContain(campaignFails ? "Campaign failed." : "Fall evaluations");
        cut.FindAll(".region-failure").Count.ShouldBe(
            Convert.ToInt32(identityFails) + Convert.ToInt32(seasonFails) + Convert.ToInt32(campaignFails));
    }

    [Fact]
    public void Render_ShowsRoleSpecificNoSeasonRecovery_AndSuppressesCampaignCreation()
    {
        Configure(isAdministrator: true, season: EmptySeason(), campaigns: EmptyCampaigns());

        var cut = RenderOverview();

        cut.Markup.ShouldContain("No current season");
        cut.Markup.ShouldContain("Go to Seasons");
        cut.Markup.ShouldContain("Season required");
        cut.Markup.ShouldNotContain("Create campaign");
    }

    [Fact]
    public void Render_ShowsNoActiveCampaignRecovery_ForAdministratorWithCurrentSeason()
    {
        Configure(isAdministrator: true, campaigns: EmptyCampaigns());

        var cut = RenderOverview();

        cut.Markup.ShouldContain("No campaign is active");
        cut.Markup.ShouldContain("View campaigns");
        cut.Markup.ShouldContain("Create campaign");
    }

    [Fact]
    public void Render_DoesNotPresentHistoricalFirstRowAsCurrent_ForMember()
    {
        var historical = CurrentSeason();
        historical = new ServiceResult<SeasonPageResult>(historical.Value with
        {
            Items = [historical.Value.Items[0] with { IsCurrent = false }]
        });
        Configure(isAdministrator: false, season: historical, campaigns: EmptyCampaigns());

        var cut = RenderOverview();

        cut.Markup.ShouldContain("No current season");
        cut.Markup.ShouldContain("A club administrator establishes the current season.");
        cut.Markup.ShouldContain("Browse teams");
        cut.Markup.ShouldNotContain("Go to Seasons");
        cut.Markup.ShouldNotContain("Create campaign");
    }

    [Fact]
    public void RetryIdentity_ReloadsOnlyIdentity_AndPreservesSuccessfulRegions()
    {
        var identity = Substitute.For<IClubIdentityQueryService>();
        identity.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new ServiceResult<ClubIdentityResult>(ServiceProblem.ServerError("Identity unavailable."))),
            Task.FromResult(new ServiceResult<ClubIdentityResult>(Identity())));
        var services = Configure(isAdministrator: false, identity: identity);
        var cut = RenderOverview();

        cut.Markup.ShouldContain("Identity unavailable.");
        cut.Markup.ShouldContain("2026–27");
        cut.Find(".region-failure a").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("North Star Volleyball Club"));
        services.Identity.Received(2).GetCurrentAsync(Arg.Any<CancellationToken>());
        services.Seasons.Received(1).ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>());
        services.Campaigns.Received(1).GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_RestoresPersistedRegions_WithoutRepeatingStartupQueries()
    {
        var services = Configure(isAdministrator: false);

        var cut = Render<PersistedStateClubOverview>(parameters => parameters
            .Add(component => component.StartInitialized, true));

        cut.Markup.ShouldContain("North Star Volleyball Club");
        cut.Markup.ShouldContain("2026–27");
        cut.Markup.ShouldContain("Fall evaluations");
        services.Identity.DidNotReceive().GetCurrentAsync(Arg.Any<CancellationToken>());
        services.Seasons.DidNotReceive().ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>());
        services.Campaigns.DidNotReceive().GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_RestoresPersistedIdentityError_WithoutRepeatingStartupQueries()
    {
        var services = Configure(isAdministrator: false);

        var cut = Render<PersistedIdentityErrorClubOverview>(parameters => parameters
            .Add(component => component.StartInitialized, true));

        cut.Markup.ShouldContain("Identity unavailable.");
        cut.Markup.ShouldContain("Retry this section");
        cut.Markup.ShouldContain("2026–27");
        cut.Markup.ShouldContain("Fall evaluations");
        services.Identity.DidNotReceive().GetCurrentAsync(Arg.Any<CancellationToken>());
        services.Seasons.DidNotReceive().ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>());
        services.Campaigns.DidNotReceive().GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_ReconcilesRoleGatedContent_WhenAuthenticationChanges()
    {
        Configure(isAdministrator: false);
        var auth = new TestAuthenticationStateProvider(MemberPrincipal());
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = RenderOverview();
        cut.Markup.ShouldNotContain(">Crest<");

        auth.Change(AdministratorPrincipal());

        cut.WaitForAssertion(() => cut.Markup.ShouldContain(">Crest<"));
        cut.Markup.ShouldContain("href=\"/club/seasons\"");
    }

    [Fact]
    public void Render_SuppressesAdministratorContent_WhenRoleIsRevoked()
    {
        Configure(isAdministrator: true);
        var auth = new TestAuthenticationStateProvider(AdministratorPrincipal());
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = RenderOverview();
        cut.Markup.ShouldContain(">Crest<");

        auth.Change(MemberPrincipal());

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain(">Crest<"));
        cut.Markup.ShouldNotContain("href=\"/club/seasons\"");
    }

    [Fact]
    public void Render_RedirectsToAccessDenied_WhenMembershipDisappears()
    {
        var identity = Substitute.For<IClubIdentityQueryService>();
        identity.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new ServiceResult<ClubIdentityResult>(Identity())),
            Task.FromResult(new ServiceResult<ClubIdentityResult>(ServiceProblem.Forbidden("A current club membership is required."))));
        Configure(isAdministrator: false, identity: identity);
        var auth = new TestAuthenticationStateProvider(MemberPrincipal());
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = RenderOverview();
        cut.Markup.ShouldContain("North Star Volleyball Club");

        auth.Change(MemberPrincipal(clubId: null));

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldEndWith("/Account/AccessDenied"));
    }

    [Fact]
    public void Render_ReloadsAllRegions_WhenClubMembershipChanges()
    {
        var identity = Substitute.For<IClubIdentityQueryService>();
        identity.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new ServiceResult<ClubIdentityResult>(Identity())),
            Task.FromResult(new ServiceResult<ClubIdentityResult>(Identity() with { ClubId = 2, Name = "Harbor Lights Volleyball Club" })));
        var services = Configure(isAdministrator: false, identity: identity);
        var auth = new TestAuthenticationStateProvider(MemberPrincipal());
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = RenderOverview();
        cut.Markup.ShouldContain("North Star Volleyball Club");

        auth.Change(MemberPrincipal(clubId: "2"));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Harbor Lights Volleyball Club"));
        services.Identity.Received(2).GetCurrentAsync(Arg.Any<CancellationToken>());
        services.Seasons.Received(2).ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>());
        services.Campaigns.Received(2).GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_InvalidatesPersistedState_WhenClubMembershipChanges()
    {
        var identity = Substitute.For<IClubIdentityQueryService>();
        identity.GetCurrentAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new ServiceResult<ClubIdentityResult>(Identity() with { ClubId = 2, Name = "Harbor Lights Volleyball Club" })));
        var services = Configure(isAdministrator: false, identity: identity);
        var auth = new TestAuthenticationStateProvider(MemberPrincipal());
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = Render<PersistedStateClubOverview>(parameters => parameters
            .Add(component => component.StartInitialized, true));
        cut.Markup.ShouldContain("North Star Volleyball Club");

        auth.Change(MemberPrincipal(clubId: "2"));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Harbor Lights Volleyball Club"));
    }

    private IRenderedComponent<ContainerFragment> RenderOverview()
        => Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent<ClubOverview>(0);
                child.CloseComponent();
            }));
            builder.CloseComponent();
        });

    private ServiceSet Configure(
        bool isAdministrator,
        IClubIdentityQueryService? identity = null,
        ServiceResult<SeasonPageResult>? season = null,
        ServiceResult<CampaignListResult>? campaigns = null)
    {
        if (identity is null)
        {
            identity = Substitute.For<IClubIdentityQueryService>();
            identity.GetCurrentAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<ClubIdentityResult>(Identity())));
        }
        var seasonService = Substitute.For<ISeasonQueryService>();
        seasonService.ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(season ?? CurrentSeason()));
        var campaignService = Substitute.For<ICampaignQueryService>();
        campaignService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(campaigns ?? ActiveCampaigns()));

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "10"), new(NovaClaimTypes.ClubId, "1") };
        if (isAdministrator)
        {
            claims.Add(new Claim(ClaimTypes.Role, Roles.ClubAdmin));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        var auth = Substitute.For<AuthenticationStateProvider>();
        auth.GetAuthenticationStateAsync().Returns(Task.FromResult(new AuthenticationState(principal)));

        Services.AddSingleton(identity);
        Services.AddSingleton(seasonService);
        Services.AddSingleton(campaignService);
        Services.AddSingleton(auth);
        Services.AddSingleton<IAuthorizationPolicyProvider>(new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions())));
        Services.AddSingleton<IAuthorizationService>(new RoleAuthorizationService());
        return new(identity, seasonService, campaignService);
    }

    private static ClaimsPrincipal MemberPrincipal(string? clubId = "1")
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "10") };
        if (clubId is not null)
        {
            claims.Add(new Claim(NovaClaimTypes.ClubId, clubId));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal AdministratorPrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "10"),
            new(NovaClaimTypes.ClubId, "1"),
            new(ClaimTypes.Role, Roles.ClubAdmin)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private sealed class TestAuthenticationStateProvider(ClaimsPrincipal initialPrincipal)
        : AuthenticationStateProvider
    {
        private Task<AuthenticationState> _state = Task.FromResult(new AuthenticationState(initialPrincipal));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state;

        public void Change(ClaimsPrincipal principal)
            => NotifyAuthenticationStateChanged(_state = Task.FromResult(new AuthenticationState(principal)));
    }

    private static ClubIdentityResult Identity() => new()
    {
        ClubId = 1,
        Name = "North Star Volleyball Club",
        City = "Duluth",
        State = "MN",
        HasCrest = false
    };

    private static ServiceResult<SeasonPageResult> CurrentSeason() => new(new SeasonPageResult
    {
        Items = [new SeasonSummary { SeasonId = 2, Name = "2026–27", StartDate = new(2026, 9, 1), EndDate = new(2027, 5, 31), IsCurrent = true, ConcurrencyToken = Guid.NewGuid() }],
        Page = 1,
        PageSize = 1,
        TotalCount = 1
    });

    private static ServiceResult<SeasonPageResult> EmptySeason() => new(new SeasonPageResult
    {
        Items = [],
        Page = 1,
        PageSize = 1,
        TotalCount = 0
    });

    private static ServiceResult<CampaignListResult> ActiveCampaigns() => new(new CampaignListResult
    {
        TotalCount = 1,
        Seasons = [new CampaignSeasonGroup
        {
            SeasonId = 2, Name = "2026–27", StartDate = new(2026, 9, 1), EndDate = new(2027, 5, 31), ConcurrencyToken = Guid.NewGuid(),
            Campaigns = [new CampaignListItem { CampaignId = 3, Name = "Fall evaluations", StartDate = new(2026, 9, 8), Status = CampaignStatus.Active, ParticipantCount = 20, UnresolvedCount = 4 }]
        }]
    });

    private static ServiceResult<CampaignListResult> EmptyCampaigns() => new(new CampaignListResult { TotalCount = 0, Seasons = [] });

    private sealed record ServiceSet(IClubIdentityQueryService Identity, ISeasonQueryService Seasons, ICampaignQueryService Campaigns);

    private sealed class PersistedIdentityErrorClubOverview(
        IClubIdentityQueryService identityQueryService,
        ISeasonQueryService seasonQueryService,
        ICampaignQueryService campaignQueryService,
        AuthenticationStateProvider authenticationStateProvider,
        NavigationManager navigationManager)
        : ClubOverview(identityQueryService, seasonQueryService, campaignQueryService, authenticationStateProvider, navigationManager)
    {
        [Parameter] public bool StartInitialized { get; set; }

        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                PersistedClubId = "1";
                PersistedIdentity = null;
                PersistedIdentityError = "Identity unavailable.";
                PersistedSeason = CurrentSeason().Value.Items[0];
                PersistedCampaigns = ActiveCampaigns().Value;
            }

            return base.OnInitializedAsync();
        }
    }

    private sealed class PersistedStateClubOverview(
        IClubIdentityQueryService identityQueryService,
        ISeasonQueryService seasonQueryService,
        ICampaignQueryService campaignQueryService,
        AuthenticationStateProvider authenticationStateProvider,
        NavigationManager navigationManager)
        : ClubOverview(identityQueryService, seasonQueryService, campaignQueryService, authenticationStateProvider, navigationManager)
    {
        [Parameter] public bool StartInitialized { get; set; }

        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                PersistedClubId = "1";
                PersistedIdentity = Identity();
                PersistedSeason = CurrentSeason().Value.Items[0];
                PersistedCampaigns = ActiveCampaigns().Value;
            }

            return base.OnInitializedAsync();
        }
    }

    private sealed class RoleAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(policyName == Roles.ClubAdmin && !user.IsInRole(Roles.ClubAdmin)
                ? AuthorizationResult.Failed()
                : AuthorizationResult.Success());
    }
}
