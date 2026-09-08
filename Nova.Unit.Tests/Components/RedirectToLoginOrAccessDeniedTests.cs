using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.Components;
using Nova.SharedKernel.Security;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Components;

/// <summary>
/// Tests for <see cref="RedirectToLoginOrAccessDenied"/>.
/// </summary>
public class RedirectToLoginOrAccessDeniedTests : BunitContext
{
    [Fact]
    public void OnInitializedAsyncNavigatesToLoginWhenUserIsAnonymous()
    {
        // Arrange
        SetAuthenticationState(isAuthenticated: false);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        var currentUri = navigationManager.Uri;
        var expectedUri = navigationManager.ToAbsoluteUri(
            $"/Account/Login?returnUrl={Uri.EscapeDataString(currentUri)}").ToString();

        // Act
        Render<RedirectToLoginOrAccessDenied>();

        // Assert
        navigationManager.Uri.ShouldBe(expectedUri);
    }

    [Fact]
    public void OnInitializedAsyncNavigatesToAccessDeniedWhenUserIsAuthenticated()
    {
        // Arrange
        SetAuthenticationState(isAuthenticated: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        var expectedUri = navigationManager.ToAbsoluteUri("/Account/AccessDenied").ToString();

        // Act
        Render<RedirectToLoginOrAccessDenied>();

        // Assert
        navigationManager.Uri.ShouldBe(expectedUri);
    }

    [Fact]
    public void OnInitializedAsyncNavigatesDemotedMemberToClubNoticeOnAdministratorRoute()
    {
        SetAuthenticationState(isAuthenticated: true, hasClub: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/club/seasons");

        Render<RedirectToLoginOrAccessDenied>();

        navigationManager.Uri.ShouldBe(navigationManager.ToAbsoluteUri("/club?notice=permissions-changed").ToString());
    }

    [Fact]
    public void OnInitializedAsyncNavigatesDemotedMemberToClubNoticeOnLegacyAdministratorRoute()
    {
        SetAuthenticationState(isAuthenticated: true, hasClub: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/Clubs/42/admin");

        Render<RedirectToLoginOrAccessDenied>();

        navigationManager.Uri.ShouldBe(navigationManager.ToAbsoluteUri("/club?notice=permissions-changed").ToString());
    }

    [Fact]
    public void OnInitializedAsyncNavigatesDemotedMemberToAccessDeniedOnLegacyMemberRoute()
    {
        SetAuthenticationState(isAuthenticated: true, hasClub: false);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/Clubs/42");

        Render<RedirectToLoginOrAccessDenied>();

        navigationManager.Uri.ShouldBe(navigationManager.ToAbsoluteUri("/Account/AccessDenied").ToString());
    }

    private void SetAuthenticationState(bool isAuthenticated, bool hasClub = false)
    {
        Claim[] claims = hasClub
            ? [new Claim(ClaimTypes.NameIdentifier, "123"), new Claim(NovaClaimTypes.ClubId, "42")]
            : [new Claim(ClaimTypes.NameIdentifier, "123")];
        var identity = isAuthenticated
            ? new ClaimsIdentity(
                claims,
                "TestAuth")
            : new ClaimsIdentity();

        var authProvider = Substitute.For<AuthenticationStateProvider>();
        authProvider.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));
        Services.AddSingleton(authProvider);
    }
}
