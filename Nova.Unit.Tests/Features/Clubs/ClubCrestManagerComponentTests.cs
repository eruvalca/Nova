using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Photos;
using Nova.Shared.Results;
using Nova.UI.Features.Clubs.Components;
using NSubstitute;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Features.Clubs;

/// <summary>
/// Component-level tests for the club crest management island: placeholder/preview rendering, upload
/// with client-side validation, change and remove flows, and access-denied redirects.
/// </summary>
public sealed class ClubCrestManagerComponentTests : BunitContext
{
    private const long ClubId = 42;

    [Fact]
    public void Render_ShowsPlaceholder_WhenClubHasNoCrest()
    {
        var crestService = Substitute.For<IClubCrestService>();
        Services.AddSingleton(crestService);

        var cut = Render<ClubCrestManager>(parameters => parameters
            .Add(p => p.ClubId, ClubId)
            .Add(p => p.HasCrest, false));

        cut.Markup.ShouldContain("club-crest-placeholder");
        cut.Markup.ShouldContain("This club does not have a crest yet.");
        cut.Markup.ShouldContain("Upload crest");
        cut.Markup.ShouldNotContain("Remove crest");
    }

    [Fact]
    public void Render_ShowsCurrentCrest_WhenClubHasCrest()
    {
        var crestService = Substitute.For<IClubCrestService>();
        Services.AddSingleton(crestService);

        var cut = Render<ClubCrestManager>(parameters => parameters
            .Add(p => p.ClubId, ClubId)
            .Add(p => p.HasCrest, true));

        cut.Find("img.club-crest-preview").GetAttribute("src")
            .ShouldBe($"/api/clubs/{ClubId}/crest?size=medium");
        cut.Markup.ShouldContain("Change crest");
        cut.Markup.ShouldContain("Remove crest");
        cut.Markup.ShouldNotContain("This club does not have a crest yet.");
    }

    [Fact]
    public void FileSelection_ValidatesSize_AndShowsError()
    {
        var crestService = Substitute.For<IClubCrestService>();
        Services.AddSingleton(crestService);

        var cut = Render<ClubCrestManager>(parameters => parameters
            .Add(p => p.ClubId, ClubId)
            .Add(p => p.HasCrest, false));

        var oversized = InputFileContent.CreateFromBinary(
            new byte[ProfilePhotoConstraints.MaxBytes + 1],
            "crest.jpg",
            null,
            "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(oversized);

        cut.WaitForAssertion(() =>
            cut.Markup.ShouldContain("The crest must be between 1 byte and 10 MB."));
    }

    [Fact]
    public void Change_UploadsValidFile_CallsServiceAndFlipsToPreview()
    {
        var crestService = Substitute.For<IClubCrestService>();
        crestService.ChangeClubCrestAsync(ClubId, Arg.Any<ClubCrestUpload>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));
        Services.AddSingleton(crestService);

        var cut = Render<ClubCrestManager>(parameters => parameters
            .Add(p => p.ClubId, ClubId)
            .Add(p => p.HasCrest, false));

        var jpeg = InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(jpeg);
        cut.WaitForAssertion(() =>
            cut.Markup.ShouldContain("data:image/jpeg;base64,"));

        cut.Find("button[type='button'].btn-primary").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Club crest updated."));
        cut.Find("img.club-crest-preview").GetAttribute("src")
            .ShouldBe($"/api/clubs/{ClubId}/crest?size=medium");
        crestService.Received(1).ChangeClubCrestAsync(
            ClubId,
            Arg.Is<ClubCrestUpload>(upload => upload.ContentType == "image/jpeg" && upload.Content.Length > 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Change_ShowsValidationProblem_WhenServiceRejectsCrest()
    {
        var crestService = Substitute.For<IClubCrestService>();
        crestService.ChangeClubCrestAsync(ClubId, Arg.Any<ClubCrestUpload>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(
                ServiceProblem.Validation("crest", ["Only JPEG, PNG, and WebP images are allowed."]))));
        Services.AddSingleton(crestService);

        var cut = Render<ClubCrestManager>(parameters => parameters
            .Add(p => p.ClubId, ClubId)
            .Add(p => p.HasCrest, false));

        var jpeg = InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(jpeg);
        cut.WaitForAssertion(() =>
            cut.Markup.ShouldContain("data:image/jpeg;base64,"));

        cut.Find("button[type='button'].btn-primary").Click();

        cut.WaitForAssertion(() =>
            cut.Markup.ShouldContain("Only JPEG, PNG, and WebP images are allowed."));
        cut.Markup.ShouldContain("Upload crest");
    }

    [Fact]
    public void Remove_ConfirmsThenCallsServiceAndFlipsToPlaceholder()
    {
        var crestService = Substitute.For<IClubCrestService>();
        crestService.RemoveClubCrestAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));
        Services.AddSingleton(crestService);

        var cut = Render<ClubCrestManager>(parameters => parameters
            .Add(p => p.ClubId, ClubId)
            .Add(p => p.HasCrest, true));

        var removeButton = cut.FindAll("button").Single(button => button.TextContent.Trim() == "Remove crest");
        removeButton.Click();
        cut.Markup.ShouldContain("Remove the club crest?");

        cut.Find("button.btn-danger").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Club crest removed."));
        cut.Markup.ShouldContain("club-crest-placeholder");
        crestService.Received(1).RemoveClubCrestAsync(ClubId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Change_RedirectsToAccessDenied_WhenServiceReturnsForbidden()
    {
        var crestService = Substitute.For<IClubCrestService>();
        crestService.ChangeClubCrestAsync(ClubId, Arg.Any<ClubCrestUpload>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(ServiceProblem.Forbidden())));
        Services.AddSingleton(crestService);

        var cut = Render<ClubCrestManager>(parameters => parameters
            .Add(p => p.ClubId, ClubId)
            .Add(p => p.HasCrest, false));

        var jpeg = InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(jpeg);
        cut.WaitForAssertion(() =>
            cut.Markup.ShouldContain("data:image/jpeg;base64,"));

        cut.Find("button[type='button'].btn-primary").Click();

        // bUnit wires the NavigationManager through its own proxy; assert the navigation target
        // landed on the access-denied URL via the (real) registered manager.
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() =>
            navigationManager.Uri.ShouldBe("http://localhost/Account/AccessDenied"));
    }
}
