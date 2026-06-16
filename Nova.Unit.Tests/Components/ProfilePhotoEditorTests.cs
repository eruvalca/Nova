using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nova.Shared.Photos;
using Nova.Shared.Results;
using Nova.UI.Features.Account.Components;
using Shouldly;

namespace Nova.Unit.Tests.Components;

public class ProfilePhotoEditorTests : BunitContext
{
    private sealed class PersistedStateProfilePhotoEditor(
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
    public void OnInitializedAsync_DoesNotFetchPhoto_WhenPersistedStateIsInitialized()
    {
        // Arrange
        var photoService = Substitute.For<IProfilePhotoService>();
        const string persistedPhotoUrl = "/api/users/42/photo?size=medium";
        Services.AddSingleton(photoService);

        // Act
        Render<PersistedStateProfilePhotoEditor>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedExistingPhotoUrl, persistedPhotoUrl));

        // Assert
        photoService.DidNotReceive().GetCurrentUserPhotoAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_ShowsExistingPhotoPreview_WhenPersistedPhotoUrlExists()
    {
        // Arrange
        var photoService = Substitute.For<IProfilePhotoService>();
        const string persistedPhotoUrl = "/api/users/42/photo?size=medium";
        Services.AddSingleton(photoService);

        // Act
        var cut = Render<PersistedStateProfilePhotoEditor>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedExistingPhotoUrl, persistedPhotoUrl));

        // Assert
        var currentPhoto = cut.Find("img.profile-photo-current");
        currentPhoto.GetAttribute("src").ShouldBe(persistedPhotoUrl);
        currentPhoto.GetAttribute("alt").ShouldBe("Your current profile photo");
        cut.Markup.ShouldContain("Choose a photo");
    }

    [Fact]
    public void OnInitializedAsync_DoesNotRenderExistingPhotoPreview_WhenNoPhotoExists()
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
            photoService.Received(1).GetCurrentUserPhotoAsync(Arg.Any<CancellationToken>());
            cut.FindAll("img.profile-photo-current").Count.ShouldBe(0);
            cut.Markup.ShouldContain("Choose a photo");
        });
    }
}
