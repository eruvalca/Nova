using System.Reflection;
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
        var heading = cut.FindAll("h1").FirstOrDefault(h => h.TextContent.Trim() == "Profile photo");
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
        cut.FindAll("h1").ShouldContain(heading => heading.TextContent.Trim() == "Profile photo");
        cut.Markup.ShouldContain("profile-photo-editor");
        cut.Markup.ShouldContain("Choose a photo");
    }

    /// <summary>
    /// The <see cref="ProfilePhotoEditor"/> island must stay interactive: it owns the
    /// choose/crop/save event handlers (see <c>ProfilePhotoEditor.razor</c>), so the host page
    /// must apply <c>@rendermode="InteractiveAuto"</c> to the editor element. bUnit invokes
    /// callbacks regardless of the deployed render mode, so the static markup assertions in this
    /// class cannot prove the island is interactive — this source assertion reads the whole
    /// <c>ProfilePhoto.razor</c> file and fails if the attribute is removed from the editor
    /// element or changed, matching the
    /// <c>TagDefinitionManagerComponentTests.ClubAdminRoute_DeclaresInteractiveAutoRenderMode</c>
    /// and <c>CampaignComponentsTests</c> conventions for interactive islands hosted by static
    /// SSR pages. The end-to-end interactivity of the island is additionally proven by browser
    /// test NB13.
    /// </summary>
    [Fact]
    public void ProfilePhoto_EditorIsland_DeclaresInteractiveAutoRenderMode()
    {
        var razorPath = Path.Join(FindRepoRoot(), "Nova", "Components", "Account", "Pages", "Manage", "ProfilePhoto.razor");
        var razorMarkup = File.ReadAllText(razorPath);

        razorMarkup.ShouldContain("<ProfilePhotoEditor");
        razorMarkup.ShouldContain("@rendermode=\"InteractiveAuto\"");
    }

    /// <summary>
    /// The page itself is static SSR (the interactive upgrade happens on the <see cref="ProfilePhotoEditor"/>
    /// island, which lives in Nova.UI and resolves on the WASM side) — a page-level
    /// <c>@rendermode InteractiveAuto</c> here would record <c>assembly = "Nova"</c>, which is
    /// absent from the WASM bundle, and the interactive upgrade would throw once the auto marker
    /// resolves to webassembly (issue #156 regression guard). The same reflection shape asserts
    /// either intent: a page declaring <c>@rendermode</c> carries a compiler-generated
    /// <see cref="RenderModeAttribute"/>; a static SSR page carries none.
    /// </summary>
    [Fact]
    public void ProfilePhoto_IsStaticSsr_WithNoPageLevelRenderMode()
    {
        var attribute = typeof(ProfilePhoto)
            .GetCustomAttributes(inherit: false)
            .OfType<RenderModeAttribute>()
            .SingleOrDefault();

        attribute.ShouldBeNull();
    }

    /// <summary>
    /// Locates the repository root by walking up from the test output directory until a git
    /// marker is found (same helper shape as the other render-mode assertions in this suite).
    /// </summary>
    /// <returns>The repository root directory.</returns>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitDirectoryPath = Path.Join(directory.FullName, ".git");
            if (Directory.Exists(gitDirectoryPath) || File.Exists(gitDirectoryPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for the ProfilePhoto render-mode assertion.");
    }
}
