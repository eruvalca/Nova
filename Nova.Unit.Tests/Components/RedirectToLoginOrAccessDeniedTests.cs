using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.Components;
using Nova.Shared.Security;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Components;

/// <summary>
/// Tests for <see cref="RedirectToLoginOrAccessDenied"/>.
/// </summary>
public class RedirectToLoginOrAccessDeniedTests : BunitContext
{
    [Fact]
    public void OnInitializedAsync_NavigatesToLogin_WhenUserIsAnonymous()
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
    public void OnInitializedAsync_NavigatesToAccessDenied_WhenUserIsAuthenticated()
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
    public void OnInitializedAsync_NavigatesDemotedMemberToClubNotice_OnAdministratorRoute()
    {
        SetAuthenticationState(isAuthenticated: true, hasClub: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/club/seasons");

        Render<RedirectToLoginOrAccessDenied>();

        navigationManager.Uri.ShouldBe(navigationManager.ToAbsoluteUri("/club?notice=permissions-changed").ToString());
    }

    private void SetAuthenticationState(bool isAuthenticated, bool hasClub = false)
    {
        var identity = isAuthenticated
            ? new ClaimsIdentity(
                hasClub
                    ? [new Claim(ClaimTypes.NameIdentifier, "123"), new Claim(NovaClaimTypes.ClubId, "42")]
                    : [new Claim(ClaimTypes.NameIdentifier, "123")],
                "TestAuth")
            : new ClaimsIdentity();

        var authProvider = Substitute.For<AuthenticationStateProvider>();
        authProvider.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));
        Services.AddSingleton(authProvider);
    }
}
