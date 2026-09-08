using Bunit;
using Cropper.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Features.Photos;
using Nova.SharedKernel.Results;
using Nova.UI.Common;
using Nova.UI.Features.Account.Components;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Components;

public class ProfilePhotoEditorTests : BunitContext
{
    [Fact]
    public async Task SaveWaitsForCropperReadinessForEachSelectedImageAsync()
    {
        var photoService = Substitute.For<IProfilePhotoService>();
        Services.AddSingleton(photoService);
        Services.AddSingleton(Substitute.For<ICropperJsInterop>());
        var cut = Render<PersistedStateProfilePhotoEditor>(parameters => parameters
            .Add(component => component.StartInitialized, true));
        var image = InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "photo.jpg", null, "image/jpeg");

        cut.FindComponent<InputFile>().UploadFiles(image);
        await cut.WaitForAssertionAsync(() =>
            cut.Find("button.btn-primary").HasAttribute("disabled").ShouldBeTrue());

        await cut.InvokeAsync(() => cut.FindComponent<NovaCropperComponent>().Instance.SimulateReady());
        await cut.WaitForAssertionAsync(() =>
            cut.Find("button.btn-primary").HasAttribute("disabled").ShouldBeFalse());

        await cut.FindAll("button").Single(button =>
                string.Equals(button.TextContent.Trim(), "Choose a different photo", StringComparison.Ordinal))
            .ClickAsync(new MouseEventArgs());
        cut.FindComponent<InputFile>().UploadFiles(image);
        await cut.WaitForAssertionAsync(() =>
            cut.Find("button.btn-primary").HasAttribute("disabled").ShouldBeTrue());
        _ = photoService.DidNotReceive().SaveProfilePhotoAsync(Arg.Any<ProfilePhotoUpload>(), Arg.Any<CancellationToken>());
    }

#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
    private sealed class PersistedStateProfilePhotoEditor(
#pragma warning restore CA1812
        IProfilePhotoService photoService,
        NavigationManager navigationManager)
        : ProfilePhotoEditor(photoService, navigationManager)
    {
        [Parameter]
        public bool StartInitialized { get; set; }

        [Parameter]
        public string? PersistedExistingPhotoUrl { get; set; }

        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                ExistingPhotoUrl = PersistedExistingPhotoUrl;
            }

            return base.OnInitializedAsync();
        }
    }

    [Fact]
    public void OnInitializedAsyncDoesNotFetchPhotoWhenPersistedStateIsInitialized()
    {
        // Arrange
        var photoService = Substitute.For<IProfilePhotoService>();
        const string PersistedPhotoUrl = "/api/users/42/photo?size=medium";
        Services.AddSingleton(photoService);

        // Act
        Render<PersistedStateProfilePhotoEditor>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedExistingPhotoUrl, PersistedPhotoUrl));

        // Assert
        _ = photoService.DidNotReceive().GetCurrentUserPhotoAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RenderShowsExistingPhotoPreviewWhenPersistedPhotoUrlExists()
    {
        // Arrange
        var photoService = Substitute.For<IProfilePhotoService>();
        const string PersistedPhotoUrl = "/api/users/42/photo?size=medium";
        Services.AddSingleton(photoService);

        // Act
        var cut = Render<PersistedStateProfilePhotoEditor>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedExistingPhotoUrl, PersistedPhotoUrl));

        // Assert
        var currentPhoto = cut.Find("img.profile-photo-current");
        currentPhoto.GetAttribute("src").ShouldBe(PersistedPhotoUrl);
        currentPhoto.GetAttribute("alt").ShouldBe("Your current profile photo");
        cut.Markup.ShouldContain("Choose a photo");
    }

    [Fact]
    public void OnInitializedAsyncDoesNotRenderExistingPhotoPreviewWhenNoPhotoExists()
    {
        // Arrange
        var photoService = Substitute.For<IProfilePhotoService>();
        photoService.GetCurrentUserPhotoAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ProfilePhotoInfo>(ServiceProblem.NotFound())));
        Services.AddSingleton(photoService);

        // Act
        var cut = Render<ProfilePhotoEditor>();

        // Assert
        cut.WaitForAssertion(() =>
        {
            _ = photoService.Received(1).GetCurrentUserPhotoAsync(Arg.Any<CancellationToken>());
            cut.FindAll("img.profile-photo-current").Count.ShouldBe(0);
            cut.Markup.ShouldContain("photo-route");
            cut.Markup.ShouldContain("Choose a photo");
            cut.Markup.ShouldContain("Frame and save");
        });
    }
}
