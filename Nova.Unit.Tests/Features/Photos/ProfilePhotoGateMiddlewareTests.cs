using Microsoft.AspNetCore.Http;
using Nova.Features.Photos;
using Shouldly;

namespace Nova.Unit.Tests.Features.Photos;

/// <summary>
/// Tests for <see cref="ProfilePhotoGateMiddleware.ShouldRedirect"/>: the gate only redirects
/// authenticated users without the photo claim, and exempts account/API/framework/static paths.
/// </summary>
public class ProfilePhotoGateMiddlewareTests
{
    [Fact]
    public void ShouldRedirect_ReturnsFalse_WhenUnauthenticated()
    {
        ProfilePhotoGateMiddleware.ShouldRedirect("/", isAuthenticated: false, hasPhotoClaim: false)
            .ShouldBeFalse();
    }

    [Fact]
    public void ShouldRedirect_ReturnsFalse_WhenUserHasPhotoClaim()
    {
        ProfilePhotoGateMiddleware.ShouldRedirect("/", isAuthenticated: true, hasPhotoClaim: true)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/clubs")]
    [InlineData("/players/42")]
    public void ShouldRedirect_ReturnsTrue_ForAppPagesWithoutPhotoClaim(string path)
    {
        ProfilePhotoGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: false)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData("/Account/ProfilePhoto")]
    [InlineData("/Account/ProfilePhoto/Complete")]
    [InlineData("/Account/Logout")]
    [InlineData("/account/manage")]
    [InlineData("/api/account/profile-photo")]
    [InlineData("/api/users/1/photo")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/_content/Cropper.Blazor/cropper.min.js")]
    [InlineData("/_blazor")]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/not-found")]
    [InlineData("/Error")]
    [InlineData("/favicon.png")]
    [InlineData("/app.css")]
    [InlineData("/lib/bootstrap/dist/js/bootstrap.bundle.min.js")]
    public void ShouldRedirect_ReturnsFalse_ForExemptPaths(string path)
    {
        ProfilePhotoGateMiddleware.ShouldRedirect(new PathString(path), isAuthenticated: true, hasPhotoClaim: false)
            .ShouldBeFalse();
    }
}
