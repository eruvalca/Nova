using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.Components;
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

    private void SetAuthenticationState(bool isAuthenticated)
    {
        var identity = isAuthenticated
            ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "123")], "TestAuth")
            : new ClaimsIdentity();

        var authProvider = Substitute.For<AuthenticationStateProvider>();
        authProvider.GetAuthenticationStateAsync()
            .Returns(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));
        Services.AddSingleton(authProvider);
    }
}
