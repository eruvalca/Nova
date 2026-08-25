using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nova.Components.Layout;
using Nova.Data.Tenancy;
using Nova.Shared.Security;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Components;

/// <summary>
/// Tests for <see cref="NavMenu"/> rendering of the authenticated club link.
/// </summary>
public class NavMenuTests
{
    [Fact]
    public void Render_RendersClubLink_WhenUserHasClubNameClaim()
    {
        // Arrange
        var currentUserProvider = Substitute.For<ICurrentUserProvider>();
        currentUserProvider.ClubId.Returns(42L);
        currentUserProvider.UserId.Returns(7L);

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(CreatePrincipal(clubId: "42", clubName: "Austin Strikers"))));

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => currentUserProvider);
        testContext.Services.AddScoped(_ => httpContextAccessor);
        testContext.Services.AddScoped(_ => authStateProvider);
        testContext.Services.AddScoped<NavigationManager, FakeNavigationManager>();
        testContext.Services.AddSingleton<IAuthorizationPolicyProvider>(new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions())));
        testContext.Services.AddSingleton<IAuthorizationService, FakeAuthorizationService>();

        // Act
        var cut = testContext.Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<NavMenu>(2);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        // Assert
        cut.Markup.ShouldContain("Austin Strikers");
        cut.Markup.ShouldContain("href=\"Clubs/42\"");
        cut.Markup.ShouldContain("href=\"campaigns\"");
        cut.Markup.ShouldContain("href=\"players\"");
        cut.Markup.ShouldContain("href=\"teams\"");

        // Each authorized link renders the dual-glyph overlay: the outline span plus its
        // -fill twin (toggled by CSS off the NavLink .active class; see NavMenu.razor.css).
        cut.Markup.ShouldContain("bi-house nav-icon");
        cut.Markup.ShouldContain("bi-house-fill nav-icon-fill");
        cut.Markup.ShouldContain("bi-building nav-icon");
        cut.Markup.ShouldContain("bi-building-fill nav-icon-fill");
        cut.Markup.ShouldContain("bi-calendar-check nav-icon");
        cut.Markup.ShouldContain("bi-calendar-check-fill nav-icon-fill");
        cut.Markup.ShouldContain("bi-people nav-icon");
        cut.Markup.ShouldContain("bi-people-fill nav-icon-fill");
        cut.Markup.ShouldContain("bi-shield nav-icon");
        cut.Markup.ShouldContain("bi-shield-fill nav-icon-fill");
        cut.Markup.ShouldContain("nav-icon-slot");
    }

    [Fact]
    public void Render_OmitsClubLink_WhenUserHasNoClubNameClaim()
    {
        // Arrange
        var currentUserProvider = Substitute.For<ICurrentUserProvider>();
        currentUserProvider.ClubId.Returns((long?)null);
        currentUserProvider.UserId.Returns(8L);

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(CreatePrincipal(clubId: null, clubName: null))));

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => currentUserProvider);
        testContext.Services.AddScoped(_ => httpContextAccessor);
        testContext.Services.AddScoped(_ => authStateProvider);
        testContext.Services.AddScoped<NavigationManager, FakeNavigationManager>();
        testContext.Services.AddSingleton<IAuthorizationPolicyProvider>(new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions())));
        testContext.Services.AddSingleton<IAuthorizationService, FakeAuthorizationService>();

        // Act
        var cut = testContext.Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<NavMenu>(2);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        // Assert
        cut.Markup.ShouldNotContain("href=\"Clubs/");
        cut.Markup.ShouldNotContain("href=\"campaigns\"");
        cut.Markup.ShouldNotContain("href=\"players\"");
        cut.Markup.ShouldNotContain("href=\"teams\"");
    }

    /// <summary>
    /// When the user's club has a crest, the club nav item renders the crest image instead of
    /// the building glyphs, pointing at the small crest variant.
    /// </summary>
    [Fact]
    public void Render_RendersClubCrestImage_WhenUserHasClubCrestClaim()
    {
        // Arrange
        var currentUserProvider = Substitute.For<ICurrentUserProvider>();
        currentUserProvider.ClubId.Returns(42L);
        currentUserProvider.UserId.Returns(7L);

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(CreatePrincipal(clubId: "42", clubName: "Austin Strikers", hasClubCrest: true))));

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => currentUserProvider);
        testContext.Services.AddScoped(_ => httpContextAccessor);
        testContext.Services.AddScoped(_ => authStateProvider);
        testContext.Services.AddScoped<NavigationManager, FakeNavigationManager>();
        testContext.Services.AddSingleton<IAuthorizationPolicyProvider>(new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions())));
        testContext.Services.AddSingleton<IAuthorizationService, FakeAuthorizationService>();

        // Act
        var cut = testContext.Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<NavMenu>(2);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        // Assert
        cut.Markup.ShouldContain("nav-avatar");
        cut.Markup.ShouldContain("src=\"/api/clubs/42/crest?size=small\"");
        // The crest image is decorative (the club name already labels the link), so it carries an
        // empty alt (bUnit serializes an empty attribute as a bare `alt`) and is hidden from
        // assistive technology with aria-hidden — never an `alt="Club crest"` that would be
        // announced redundantly and overload the link's accessible name.
        cut.Markup.ShouldContain("alt aria-hidden=\"true\"");
        cut.Markup.ShouldNotContain("Alt=\"Club crest\"");
        // The crest image replaces the building glyphs in the club nav item.
        cut.Markup.ShouldNotContain("bi-building nav-icon");
        cut.Markup.ShouldNotContain("bi-building-fill nav-icon-fill");
    }

    [Fact]
    public void Render_RendersAvatarWithPhotoUrl_WhenUserHasProfilePhotoClaim()
    {
        // Arrange
        var currentUserProvider = Substitute.For<ICurrentUserProvider>();
        currentUserProvider.ClubId.Returns(42L);
        currentUserProvider.UserId.Returns(7L);

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(CreatePrincipal(
                clubId: "42", clubName: "Austin Strikers", hasProfilePhoto: true))));

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => currentUserProvider);
        testContext.Services.AddScoped(_ => httpContextAccessor);
        testContext.Services.AddScoped(_ => authStateProvider);
        testContext.Services.AddScoped<NavigationManager, FakeNavigationManager>();
        testContext.Services.AddSingleton<IAuthorizationPolicyProvider>(new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions())));
        testContext.Services.AddSingleton<IAuthorizationService, FakeAuthorizationService>();

        // Act
        var cut = testContext.Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<NavMenu>(2);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        // Assert
        cut.Markup.ShouldContain("class=\"nav-avatar\"");
        cut.Markup.ShouldContain("src=\"/api/users/7/photo?size=small\"");
        cut.Markup.ShouldContain("alt=\"Profile photo\"");
    }

    [Fact]
    public void Render_OmitsAvatar_WhenUserHasNoProfilePhotoClaim()
    {
        // Arrange
        var currentUserProvider = Substitute.For<ICurrentUserProvider>();
        currentUserProvider.ClubId.Returns(42L);
        currentUserProvider.UserId.Returns(7L);

        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var authStateProvider = Substitute.For<AuthenticationStateProvider>();
        authStateProvider.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(CreatePrincipal(
                clubId: "42", clubName: "Austin Strikers", hasProfilePhoto: false))));

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => currentUserProvider);
        testContext.Services.AddScoped(_ => httpContextAccessor);
        testContext.Services.AddScoped(_ => authStateProvider);
        testContext.Services.AddScoped<NavigationManager, FakeNavigationManager>();
        testContext.Services.AddSingleton<IAuthorizationPolicyProvider>(new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions())));
        testContext.Services.AddSingleton<IAuthorizationService, FakeAuthorizationService>();

        // Act
        var cut = testContext.Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<NavMenu>(2);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });

        // Assert
        cut.Markup.ShouldNotContain("class=\"nav-avatar\"");
        cut.Markup.ShouldNotContain("alt=\"Profile photo\"");
    }

    private static ClaimsPrincipal CreatePrincipal(string? clubId, string? clubName, bool hasClubCrest = false, bool hasProfilePhoto = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "1"),
        };

        if (clubId is not null)
        {
            claims.Add(new Claim(NovaClaimTypes.ClubId, clubId));
        }

        if (clubName is not null)
        {
            claims.Add(new Claim(NovaClaimTypes.ClubName, clubName));
        }

        if (hasClubCrest)
        {
            claims.Add(new Claim(NovaClaimTypes.HasClubCrest, "true"));
        }

        if (hasProfilePhoto)
        {
            claims.Add(new Claim(NovaClaimTypes.HasProfilePhoto, "true"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private sealed class FakeNavigationManager : NavigationManager
    {
        public FakeNavigationManager()
        {
            Initialize("https://localhost/", "https://localhost/");
        }
    }

    private sealed class FakeAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }
}
