using Bunit;
using Cropper.Blazor.Components;
using Cropper.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Features.Photos;
using Nova.SharedKernel.Results;
using Nova.UI.Common;
using Nova.UI.Features.Clubs.Components;
using NSubstitute;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Features.Clubs;

/// <summary>
/// Component-level tests for the club crest management island: placeholder/preview rendering,
/// upload with client-side validation, the free-form crop step (save/choose-a-different-image),
/// change and remove flows, and access-denied redirects.
/// </summary>
public sealed class ClubCrestManagerComponentTests : BunitContext
{
    private const long ClubId = 42;

    [Fact]
    public void RenderShowsPlaceholderWhenClubHasNoCrest()
    {
        var crestService = Substitute.For<IClubCrestService>();
        Services.AddSingleton(crestService);

        var cut = RenderClubCrestManager(hasCrest: false);

        cut.Markup.ShouldContain("club-crest-placeholder");
        cut.Markup.ShouldContain("This club does not have a crest yet.");
        cut.FindComponent<InputFile>().ShouldNotBeNull();
        cut.Markup.ShouldNotContain("Remove crest");
        cut.Markup.ShouldNotContain("Save crest");
    }

    [Fact]
    public void RenderShowsCurrentCrestWhenClubHasCrest()
    {
        var crestService = Substitute.For<IClubCrestService>();
        Services.AddSingleton(crestService);

        var cut = RenderClubCrestManager(hasCrest: true);

        cut.Find("img.club-crest-preview").GetAttribute("src")
            .ShouldBe($"/api/clubs/{ClubId}/crest?size=medium");
        cut.FindComponent<InputFile>().ShouldNotBeNull();
        cut.Markup.ShouldContain("Remove crest");
        cut.Markup.ShouldNotContain("This club does not have a crest yet.");
        cut.Markup.ShouldNotContain("Save crest");
    }

    [Fact]
    public void FileSelectionValidatesSizeAndShowsError()
    {
        var crestService = Substitute.For<IClubCrestService>();
        Services.AddSingleton(crestService);

        var cut = RenderClubCrestManager(hasCrest: false);

        var oversized = InputFileContent.CreateFromBinary(
            new byte[ProfilePhotoConstraints.MaxBytes + 1],
            "crest.jpg",
            null,
            "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(oversized);

        cut.WaitForAssertion(() =>
            cut.Markup.ShouldContain("The crest must be between 1 byte and 10 MB."));
        cut.Markup.ShouldNotContain("Save crest");
    }

    [Fact]
    public void FileSelectionEntersCropStepWithFreeFormCropper()
    {
        var crestService = Substitute.For<IClubCrestService>();
        Services.AddSingleton(crestService);

        var cut = RenderClubCrestManager(hasCrest: false);

        var jpeg = InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(jpeg);

        // The crop step replaces the static preview with the cropper and its action buttons.
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("club-crest-cropper"));
        cut.Markup.ShouldContain("Save crest");
        cut.Markup.ShouldContain("Choose a different image");
        cut.Markup.ShouldContain("data:image/jpeg;base64,");
        cut.FindAll("img.club-crest-preview").Count.ShouldBe(0);
    }

    [Fact]
    public async Task SaveIsDisabledUntilCropperReadyAsync()
    {
        var crestService = Substitute.For<IClubCrestService>();
        Services.AddSingleton(crestService);

        var cut = RenderClubCrestManager(hasCrest: false);

        var jpeg = InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(jpeg);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Save crest"));

        // The Cropper.js instance boots asynchronously after the image loads; saving before the
        // ready signal would export against a not-yet-initialized instance, so the button stays
        // disabled until the cropper reports ready.
        var saveButton = cut.Find("button[type='button'].btn-primary");
        saveButton.HasAttribute("disabled").ShouldBeTrue("Save crest must wait for the cropper to be ready");

        await cut.InvokeAsync(() => cut.FindComponent<NovaCropperComponent>().Instance.SimulateReady());

        await cut.WaitForAssertionAsync(() =>
            cut.Find("button[type='button'].btn-primary").HasAttribute("disabled").ShouldBeFalse(
                "Save crest must be enabled once the cropper reports ready"));
    }

    [Fact]
    public void SaveCropStepChooseDifferentImageClearsSelection()
    {
        var crestService = Substitute.For<IClubCrestService>();
        Services.AddSingleton(crestService);

        var cut = RenderClubCrestManager(hasCrest: false);

        var jpeg = InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(jpeg);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("club-crest-cropper"));

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Choose a different image", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Save crest"));
        cut.Markup.ShouldNotContain("club-crest-cropper");
        cut.FindComponent<InputFile>().ShouldNotBeNull();
        _ = crestService.DidNotReceive().ChangeClubCrestAsync(Arg.Any<long>(), Arg.Any<ClubCrestUpload>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeUploadsValidFileCallsServiceAndFlipsToPreviewAsync()
    {
        var crestService = Substitute.For<IClubCrestService>();
        crestService.ChangeClubCrestAsync(ClubId, Arg.Any<ClubCrestUpload>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));
        Services.AddSingleton(crestService);

        var exporterBytes = TestImages.CreateJpeg();
        var cut = RenderClubCrestManager(hasCrest: false, exporterBytes);

        var jpeg = InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(jpeg);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Save crest"));

        var cropper = cut.FindComponent<NovaCropperComponent>();
        await cut.InvokeAsync(() => cropper.Instance.SimulateReady());
        await cut.WaitForAssertionAsync(() =>
            cut.Find("button[type='button'].btn-primary").HasAttribute("disabled").ShouldBeFalse());

#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cut.Find("button[type='button'].btn-primary").Click();
#pragma warning restore CA1849, S6966

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Club crest updated."));
        cut.Find("img.club-crest-preview").GetAttribute("src")
            .ShouldBe($"/api/clubs/{ClubId}/crest?size=medium");
        await crestService.Received(1).ChangeClubCrestAsync(
            ClubId,
            Arg.Is<ClubCrestUpload>(upload => upload.ContentType == "image/jpeg" && upload.Content.SequenceEqual(exporterBytes)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeShowsValidationProblemWhenServiceRejectsCrestAsync()
    {
        var crestService = Substitute.For<IClubCrestService>();
        crestService.ChangeClubCrestAsync(ClubId, Arg.Any<ClubCrestUpload>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(
                ServiceProblem.Validation("crest", "Only JPEG, PNG, and WebP images are allowed."))));
        Services.AddSingleton(crestService);

        var cut = RenderClubCrestManager(hasCrest: false);

        var jpeg = InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(jpeg);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Save crest"));

        await cut.InvokeAsync(() => cut.FindComponent<NovaCropperComponent>().Instance.SimulateReady());
        await cut.WaitForAssertionAsync(() =>
            cut.Find("button[type='button'].btn-primary").HasAttribute("disabled").ShouldBeFalse());

#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cut.Find("button[type='button'].btn-primary").Click();
#pragma warning restore CA1849, S6966

        await cut.WaitForAssertionAsync(() =>
            cut.Markup.ShouldContain("Only JPEG, PNG, and WebP images are allowed."));
        // The rejected crop is not lost; the user stays in the crop step to retry or re-choose.
        cut.Markup.ShouldContain("Save crest");
        cut.Markup.ShouldContain("Choose a different image");
    }

    [Fact]
    public void RemoveConfirmsThenCallsServiceAndFlipsToPlaceholder()
    {
        var crestService = Substitute.For<IClubCrestService>();
        crestService.RemoveClubCrestAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));
        Services.AddSingleton(crestService);

        var cut = RenderClubCrestManager(hasCrest: true);

        var removeButton = cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Remove crest", StringComparison.Ordinal));
        removeButton.Click();
        cut.Markup.ShouldContain("Remove the club crest?");

        cut.Find("button.btn-danger").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Club crest removed."));
        cut.Markup.ShouldContain("club-crest-placeholder");
        _ = crestService.Received(1).RemoveClubCrestAsync(ClubId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ParameterUpdateFlipsFromFalseToTrueResyncsIslandToCrest()
    {
        var crestService = Substitute.For<IClubCrestService>();
        Services.AddSingleton(crestService);

        // The host page can render the island before its summary loads while the crest flag is false;
        // when the summary later arrives the parameter changes and the island must re-sync.
        var cut = RenderClubCrestManager(hasCrest: false);
        cut.Markup.ShouldContain("club-crest-placeholder");
        cut.Markup.ShouldNotContain("Remove crest");

        cut.Render(parameters => parameters.Add(p => p.HasCrest, true));

        cut.Markup.ShouldNotContain("club-crest-placeholder");
        cut.Find("img.club-crest-preview").GetAttribute("src")
            .ShouldBe($"/api/clubs/{ClubId}/crest?size=medium");
        cut.Markup.ShouldContain("Remove crest");
    }

    [Fact]
    public async Task ParameterUpdateStaleAfterLocalSaveDoesNotRevertCrestPresenceAsync()
    {
        var crestService = Substitute.For<IClubCrestService>();
        crestService.ChangeClubCrestAsync(ClubId, Arg.Any<ClubCrestUpload>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));
        Services.AddSingleton(crestService);

        var cut = RenderClubCrestManager(hasCrest: false);

        var jpeg = InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(jpeg);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Save crest"));

        await cut.InvokeAsync(() => cut.FindComponent<NovaCropperComponent>().Instance.SimulateReady());
        await cut.WaitForAssertionAsync(() =>
            cut.Find("button[type='button'].btn-primary").HasAttribute("disabled").ShouldBeFalse());

#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cut.Find("button[type='button'].btn-primary").Click();
#pragma warning restore CA1849, S6966
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Club crest updated."));

        // A stale parameter re-render (HasCrest == false) must not revert the locally approved
        // crest back to the placeholder.
        cut.Render(parameters => parameters.Add(p => p.HasCrest, false));

        cut.Markup.ShouldContain("Club crest updated.");
        cut.Find("img.club-crest-preview").GetAttribute("src")
            .ShouldBe($"/api/clubs/{ClubId}/crest?size=medium");
        cut.Markup.ShouldNotContain("club-crest-placeholder");
    }

    [Fact]
    public void ParameterUpdateStaleAfterLocalRemoveDoesNotRevertPlaceholder()
    {
        var crestService = Substitute.For<IClubCrestService>();
        crestService.RemoveClubCrestAsync(ClubId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));
        Services.AddSingleton(crestService);

        var cut = RenderClubCrestManager(hasCrest: true);

        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Remove crest", StringComparison.Ordinal)).Click();
        cut.Find("button.btn-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Club crest removed."));

        // A stale parameter re-render (HasCrest == true) must not resurrect the removed crest.
        cut.Render(parameters => parameters.Add(p => p.HasCrest, true));

        cut.Markup.ShouldContain("Club crest removed.");
        cut.Markup.ShouldContain("club-crest-placeholder");
        cut.Markup.ShouldNotContain("Remove crest");
    }

    [Fact]
    public void CrestMutatedLocallyIsPersistentStateToSurviveCircuitReattach()
    {
        // The guard must survive circuit re-attach like HasCrestInitialized/CrestPresent:
        // a re-attach after a local save with a still-loading host summary (stale
        // HasCrest == false) must not revert CrestPresent back to the placeholder.
        var property = typeof(ClubCrestManager).GetProperty(nameof(ClubCrestManager.CrestMutatedLocally));

        var isPersistentState = property?.GetCustomAttributes(
            typeof(PersistentStateAttribute), inherit: false).Length > 0;

        isPersistentState.ShouldBeTrue(
            "CrestMutatedLocally must be [PersistentState] so the mutated guard survives circuit re-attach");
    }

    [Fact]
    public async Task ChangeRedirectsToAccessDeniedWhenServiceReturnsForbiddenAsync()
    {
        var crestService = Substitute.For<IClubCrestService>();
        crestService.ChangeClubCrestAsync(ClubId, Arg.Any<ClubCrestUpload>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(ServiceProblem.Forbidden())));
        Services.AddSingleton(crestService);

        var cut = RenderClubCrestManager(hasCrest: false);

        var jpeg = InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg");
        cut.FindComponent<InputFile>().UploadFiles(jpeg);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Save crest"));

        await cut.InvokeAsync(() => cut.FindComponent<NovaCropperComponent>().Instance.SimulateReady());
        await cut.WaitForAssertionAsync(() =>
            cut.Find("button[type='button'].btn-primary").HasAttribute("disabled").ShouldBeFalse());

#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cut.Find("button[type='button'].btn-primary").Click();
#pragma warning restore CA1849, S6966

        // bUnit wires the NavigationManager through its own proxy; assert the navigation target
        // landed on the access-denied URL via the (real) registered manager.
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        await cut.WaitForAssertionAsync(() =>
            navigationManager.Uri.ShouldBe("http://localhost/Account/AccessDenied"));
    }

    /// <summary>
    /// Renders the crest manager with the club crest service plus the cropper interop and canvas
    /// exporter substitutes required by the crop step.
    /// </summary>
    /// <param name="hasCrest">Whether the club currently has a crest.</param>
    /// <param name="exporterBytes">The bytes the canvas exporter should return, or <see langword="null"/> to use a default JPEG.</param>
    /// <returns>The rendered component.</returns>
    private IRenderedComponent<ClubCrestManager> RenderClubCrestManager(
        bool hasCrest,
        byte[]? exporterBytes = null)
    {
        Services.AddSingleton(Substitute.For<ICropperJsInterop>());

        var exporter = Substitute.For<ICropperCanvasExporter>();
        exporter.ExportAsync(Arg.Any<CropperComponent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(exporterBytes ?? TestImages.CreateJpeg()));
        Services.AddSingleton(exporter);

        return Render<ClubCrestManager>(parameters => parameters
            .Add(p => p.ClubId, ClubId)
            .Add(p => p.HasCrest, hasCrest));
    }
}
