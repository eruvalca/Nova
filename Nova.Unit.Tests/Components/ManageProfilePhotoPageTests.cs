using Bunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nova.Components.Account.Pages.Manage;
using Nova.Components.Account.Shared;
using Nova.Entities;
using Nova.Shared.Features.Photos;
using Nova.Shared.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Components;

/// <summary>
/// Tests for the account-management profile photo page
/// (<see cref="ProfilePhoto"/> at <c>/Account/Manage/ProfilePhoto</c>): the page hosts the
/// <c>ProfilePhotoEditor</c> and, via the folder <c>_Imports.razor</c>, is laid out inside the
/// shared manage frame (<c>ManageLayout</c>), so it renders the account hall — heading, lead,
/// directory wall, and working hall — exactly like the other manage pages. Regression guard for
/// issue #156: previously the route was served by a Nova.UI page with no ManageLayout and
/// rendered under the bare MainLayout.
/// </summary>
public class ManageProfilePhotoPageTests : BunitContext
{
    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("https://localhost/", "https://localhost/");
        }
    }

    private static UserManager<NovaUserEntity> CreateUserManager()
    {
        var store = Substitute.For<IUserStore<NovaUserEntity>>();
        return Substitute.For<UserManager<NovaUserEntity>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<NovaUserEntity>>(),
            new List<IUserValidator<NovaUserEntity>>(),
            new List<IPasswordValidator<NovaUserEntity>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<NovaUserEntity>>>());
    }

    private static SignInManager<NovaUserEntity> CreateSignInManager()
    {
        var signInManager = Substitute.For<SignInManager<NovaUserEntity>>(
            CreateUserManager(),
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<NovaUserEntity>>(),
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<ILogger<SignInManager<NovaUserEntity>>>(),
            Substitute.For<IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<NovaUserEntity>>());

        signInManager.GetExternalAuthenticationSchemesAsync()
            .Returns(Task.FromResult<IEnumerable<AuthenticationScheme>>([]));
        return signInManager;
    }

    [Fact]
    public void Render_RendersProfilePhotoEditor_WhenNoPhotoExists()
    {
        // Arrange
        var photoService = Substitute.For<IProfilePhotoService>();
        photoService.GetCurrentUserPhotoAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ProfilePhotoInfo>(ServiceProblem.NotFound())));
        Services.AddSingleton(photoService);
        Services.AddSingleton(CreateSignInManager());
        Services.AddSingleton<NavigationManager, TestNavigationManager>();

        // Act
        var cut = Render<ProfilePhoto>();

        // Assert: the page renders its own heading/lead and the photo editor (no stored photo →
        // the "Choose a photo" input is shown).
        var heading = cut.FindAll("h3").FirstOrDefault(h => h.TextContent.Trim() == "Profile photo");
        heading.ShouldNotBeNull("the page must render its Profile photo heading");
        cut.Markup.ShouldContain("Choose a photo");
        cut.Markup.ShouldContain("profile-photo-editor");
        photoService.Received(1).GetCurrentUserPhotoAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ManageLayout_HostsProfilePhotoPageContent_InAccountHallFrame()
    {
        // Arrange
        var photoService = Substitute.For<IProfilePhotoService>();
        photoService.GetCurrentUserPhotoAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ProfilePhotoInfo>(ServiceProblem.NotFound())));
        Services.AddSingleton(photoService);
        Services.AddSingleton(CreateSignInManager());
        Services.AddSingleton<NavigationManager, TestNavigationManager>();

        // Act: bUnit does not apply the folder _Imports @layout when rendering the page directly,
        // so render the shared manage layout with the page as its body — the same relationship the
        // router builds for every /Account/Manage page (the end-to-end wiring is asserted by the
        // browser test NB13 against the real app).
        var cut = Render<ManageLayout>(parameters => parameters
            .Add(p => p.Body, (RenderFragment)(builder =>
            {
                builder.OpenComponent<ProfilePhoto>(0);
                builder.CloseComponent();
            })));

        // Assert: the page content sits inside the shared manage frame — the account hall with the
        // "Manage your account" heading + lead, the panel-wall directory, and the working hall
        // hosting the editor — exactly like the Email/Password pages (issue #156 regression guard).
        cut.Markup.ShouldContain("account-hall");
        cut.Markup.ShouldContain("Manage your account");
        cut.Markup.ShouldContain("Change your account settings");
        cut.Markup.ShouldContain("account-panel-wall");
        cut.Markup.ShouldContain("account-working-hall");
        // The directory wall includes the Profile photo panel linking to the manage route.
        cut.Markup.ShouldContain("Account/Manage/ProfilePhoto");
        // The working hall hosts the page: its heading and the photo editor.
        cut.Markup.ShouldContain("<h3>Profile photo</h3>");
        cut.Markup.ShouldContain("profile-photo-editor");
        cut.Markup.ShouldContain("Choose a photo");
    }
}
