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
    }

    private static ClaimsPrincipal CreatePrincipal(string? clubId, string? clubName)
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
