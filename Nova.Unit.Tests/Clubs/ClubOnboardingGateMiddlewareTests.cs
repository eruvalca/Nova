using Microsoft.AspNetCore.Http;
using Nova.Features.Clubs;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Tests for <see cref="ClubOnboardingGateMiddleware.ShouldRedirect"/>: the gate only redirects
/// authenticated users with a profile photo claim but without a club ID claim, and exempts 
/// account/identity flows, API endpoints, Blazor framework assets, health checks, and the /Clubs path.
/// </summary>
public class ClubOnboardingGateMiddlewareTests
{
    [Fact]
    public void ShouldRedirect_ReturnsFalse_WhenUnauthenticated()
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect("/", isAuthenticated: false, hasPhotoClaim: false, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void ShouldRedirect_ReturnsFalse_WhenUserHasNoPhotoClaim()
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect("/", isAuthenticated: true, hasPhotoClaim: false, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void ShouldRedirect_ReturnsFalse_WhenUserAlreadyHasClubIdClaim()
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect("/", isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: true);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/dashboard")]
    [InlineData("/players")]
    [InlineData("/campaigns")]
    public void ShouldRedirect_ReturnsTrue_ForNonExemptPathsWithPhotoButNoClubId(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/Account")]
    [InlineData("/Account/Manage")]
    [InlineData("/Account/ProfilePhoto")]
    [InlineData("/Account/ProfilePhoto/Complete")]
    [InlineData("/account/login")]
    public void ShouldRedirect_ReturnsFalse_ForAccountPaths(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/api/clubs")]
    [InlineData("/api/clubs/search")]
    [InlineData("/api/users/profile")]
    [InlineData("/api/account/logout")]
    public void ShouldRedirect_ReturnsFalse_ForApiPaths(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_framework/blazor.web.js.map")]
    public void ShouldRedirect_ReturnsFalse_ForFrameworkPaths(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/_content/Cropper.Blazor/cropper.min.js")]
    [InlineData("/_content/bootstrap/css/bootstrap.min.css")]
    public void ShouldRedirect_ReturnsFalse_ForContentPaths(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/_blazor")]
    public void ShouldRedirect_ReturnsFalse_ForBlazorPaths(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public void ShouldRedirect_ReturnsFalse_ForHealthCheckPaths(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/not-found")]
    [InlineData("/Error")]
    [InlineData("/Error/404")]
    public void ShouldRedirect_ReturnsFalse_ForErrorPaths(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/favicon.ico")]
    [InlineData("/favicon.png")]
    public void ShouldRedirect_ReturnsFalse_ForFaviconPaths(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/Clubs")]
    [InlineData("/Clubs/Onboarding")]
    [InlineData("/clubs/search")]
    public void ShouldRedirect_ReturnsFalse_ForClubsPaths(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory]
    [InlineData("/styles.css")]
    [InlineData("/app.js")]
    [InlineData("/lib/bootstrap.min.js")]
    [InlineData("/images/logo.png")]
    [InlineData("/file.pdf")]
    public void ShouldRedirect_ReturnsFalse_ForStaticAssets(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void ShouldRedirect_ReturnsFalse_WhenPathHasExtensionButNoFilePrefix()
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString("/download.zip"), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }
}
