using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Features.Photos;
using Nova.Shared.Results;
using NSubstitute;
using Shouldly;
using OnboardingProfilePhoto = Nova.UI.Features.Account.Pages.ProfilePhoto;

namespace Nova.Unit.Tests.Components;

/// <summary>
/// Verifies the required profile-photo onboarding page and its interactive render-mode boundary.
/// </summary>
public class ProfilePhotoPageTests : BunitContext
{
    /// <summary>
    /// Verifies that the onboarding gate renders the focused account setup board and shared editor.
    /// </summary>
    [Fact]
    public void Render_ShowsAccountSetupBoard_WithSharedPhotoEditor()
    {
        var photoService = Substitute.For<IProfilePhotoService>();
        photoService.GetCurrentUserPhotoAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ProfilePhotoInfo>(ServiceProblem.NotFound())));
        Services.AddSingleton(photoService);

        var cut = Render<OnboardingProfilePhoto>();

        cut.Find("h1").TextContent.Trim().ShouldBe("Profile photo");
        cut.Markup.ShouldContain("profile-photo-gate-workspace");
        cut.Markup.ShouldContain("profile-photo-editor");
        cut.Markup.ShouldContain("Choose image");
        cut.Markup.ShouldContain("Frame and save");
    }

    /// <summary>
    /// Verifies that the onboarding page remains interactive because file selection and cropping
    /// require browser event handling and JavaScript interop.
    /// </summary>
    [Fact]
    public void ProfilePhoto_DeclaresInteractiveAutoRenderMode()
    {
        var attribute = typeof(OnboardingProfilePhoto)
            .GetCustomAttributes(inherit: false)
            .OfType<RenderModeAttribute>()
            .SingleOrDefault();

        attribute.ShouldNotBeNull();
        attribute.Mode.ShouldBeOfType<InteractiveAutoRenderMode>();
    }
}
