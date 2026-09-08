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
    public void ShouldRedirectReturnsFalseWhenUnauthenticated()
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect("/", isAuthenticated: false, hasPhotoClaim: false, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void ShouldRedirectReturnsFalseWhenUserHasNoPhotoClaim()
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect("/", isAuthenticated: true, hasPhotoClaim: false, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void ShouldRedirectReturnsFalseWhenUserAlreadyHasClubIdClaim()
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect("/", isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: true);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/")]
    [InlineData("/dashboard")]
    [InlineData("/players")]
    [InlineData("/campaigns")]
    public void ShouldRedirectReturnsTrueForNonExemptPathsWithPhotoButNoClubId(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeTrue();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/Account")]
    [InlineData("/Account/Manage")]
    [InlineData("/Account/ProfilePhoto")]
    [InlineData("/Account/ProfilePhoto/Complete")]
    [InlineData("/account/login")]
    public void ShouldRedirectReturnsFalseForAccountPaths(string path)
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/api/clubs")]
    [InlineData("/api/clubs/search")]
    [InlineData("/api/users/profile")]
    [InlineData("/api/account/logout")]
#pragma warning disable S4144 // Each theory names a distinct category and owns different test data; the shared assertion is intentional.
    public void ShouldRedirectReturnsFalseForApiPaths(string path)
#pragma warning restore S4144
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_framework/blazor.web.js.map")]
#pragma warning disable S4144 // Each theory names a distinct category and owns different test data; the shared assertion is intentional.
    public void ShouldRedirectReturnsFalseForFrameworkPaths(string path)
#pragma warning restore S4144
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/_content/Cropper.Blazor/cropper.min.js")]
    [InlineData("/_content/bootstrap/css/bootstrap.min.css")]
#pragma warning disable S4144 // Each theory names a distinct category and owns different test data; the shared assertion is intentional.
    public void ShouldRedirectReturnsFalseForContentPaths(string path)
#pragma warning restore S4144
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/_blazor")]
#pragma warning disable S4144 // Each theory names a distinct category and owns different test data; the shared assertion is intentional.
    public void ShouldRedirectReturnsFalseForBlazorPaths(string path)
#pragma warning restore S4144
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/health")]
    [InlineData("/alive")]
#pragma warning disable S4144 // Each theory names a distinct category and owns different test data; the shared assertion is intentional.
    public void ShouldRedirectReturnsFalseForHealthCheckPaths(string path)
#pragma warning restore S4144
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/not-found")]
    [InlineData("/Error")]
    [InlineData("/Error/404")]
#pragma warning disable S4144 // Each theory names a distinct category and owns different test data; the shared assertion is intentional.
    public void ShouldRedirectReturnsFalseForErrorPaths(string path)
#pragma warning restore S4144
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/favicon.ico")]
    [InlineData("/favicon.png")]
#pragma warning disable S4144 // Each theory names a distinct category and owns different test data; the shared assertion is intentional.
    public void ShouldRedirectReturnsFalseForFaviconPaths(string path)
#pragma warning restore S4144
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/Clubs")]
    [InlineData("/Clubs/Onboarding")]
    [InlineData("/clubs/search")]
#pragma warning disable S4144 // Each theory names a distinct category and owns different test data; the shared assertion is intentional.
    public void ShouldRedirectReturnsFalseForClubsPaths(string path)
#pragma warning restore S4144
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("/styles.css")]
    [InlineData("/app.js")]
    [InlineData("/lib/bootstrap.min.js")]
    [InlineData("/images/logo.png")]
    [InlineData("/file.pdf")]
#pragma warning disable S4144 // Each theory names a distinct category and owns different test data; the shared assertion is intentional.
    public void ShouldRedirectReturnsFalseForStaticAssets(string path)
#pragma warning restore S4144
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void ShouldRedirectReturnsFalseWhenPathHasExtensionButNoFilePrefix()
    {
        // Arrange & Act
        var result = ClubOnboardingGateMiddleware.ShouldRedirect(new PathString("/download.zip"), isAuthenticated: true, hasPhotoClaim: true, hasClubIdClaim: false);

        // Assert
        result.ShouldBeFalse();
    }
}
