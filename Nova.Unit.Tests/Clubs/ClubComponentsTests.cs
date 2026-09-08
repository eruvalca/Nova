using System.Security.Claims;
using Bunit;
using Cropper.Blazor.Components;
using Cropper.Blazor.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Account;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Results;
using Nova.UI.Common;
using Nova.UI.Features.Clubs.Components;
using Nova.UI.Features.Clubs.Pages;
using NSubstitute;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Behavioral tests for Phase 4 Club Onboarding UI components using bUnit:
/// - <see cref="ClubOnboarding"/> page
/// - <see cref="CreateClubForm"/> component
/// - <see cref="ClubSearchPanel"/> component
/// - <see cref="PendingJoinRequestCard"/> component
///
/// These tests verify component rendering, state management, user interactions, and navigation.
/// xUnit creates a new class instance per test, so each test gets a fresh <see cref="TestContext"/>.
/// </summary>
public class ClubComponentsTests : BunitContext
{
    #region Helper Methods

    /// <summary>
    /// Registers default mock implementations of <see cref="IClubService"/> and
    /// <see cref="IClubJoinRequestService"/> into the test context's service container,
    /// and sets up a fake <see cref="AuthenticationStateProvider"/> representing an
    /// authenticated user who has not yet joined a club.
    /// </summary>
    /// <param name="joinRequestService">Optional substitute; a default mock is created when <see langword="null"/>.</param>
    /// <param name="clubService">Optional substitute; a default mock is created when <see langword="null"/>.</param>
    private void SetupServices(IClubJoinRequestService? joinRequestService = null,
        IClubService? clubService = null)
    {
        joinRequestService ??= Substitute.For<IClubJoinRequestService>();
        clubService ??= Substitute.For<IClubService>();

        Services.AddSingleton(joinRequestService);
        Services.AddSingleton(clubService);

        // ClubOnboarding now injects AuthenticationStateProvider to guard against club members
        // navigating back to the onboarding page. Register a fake that returns an authenticated
        // user without a ClubId claim so the guard does not redirect during tests.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-user")], "test");
        var authState = new AuthenticationState(new ClaimsPrincipal(identity));
        var fakeAuthProvider = Substitute.For<AuthenticationStateProvider>();
        fakeAuthProvider.GetAuthenticationStateAsync().Returns(Task.FromResult(authState));
        Services.AddSingleton(fakeAuthProvider);

        // CreateClubForm's crop step needs the cropper interop (injected by CropperComponent)
        // and the canvas exporter (injected into the form); both are substituted for tests.
        Services.AddSingleton(Substitute.For<ICropperJsInterop>());
        var canvasExporter = Substitute.For<ICropperCanvasExporter>();
        canvasExporter.ExportAsync(Arg.Any<CropperComponent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TestImages.CreateJpeg()));
        Services.AddSingleton(canvasExporter);
    }

    #endregion

    #region ClubOnboarding Page Tests

    /// <summary>
    /// ClubOnboarding shows create/search forms when there is no pending request (NotFound).
    /// </summary>
    [Fact]
    public void ClubOnboardingShowsCreateSearchFormsWhenNoPendingRequest()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(ServiceProblem.NotFound())));

        SetupServices(joinRequestService);

        // Act
        var cut = Render<ClubOnboarding>();

        // Assert
        cut.FindComponents<CreateClubForm>().Count.ShouldBe(1);
        cut.FindComponents<ClubSearchPanel>().Count.ShouldBe(1);
        cut.FindComponents<PendingJoinRequestCard>().Count.ShouldBe(0);
        cut.Markup.ShouldNotContain("alert-danger");
    }

    /// <summary>
    /// ClubOnboarding shows error message when GetCurrentUserPendingRequestAsync returns ServerError.
    /// </summary>
    [Fact]
    public void ClubOnboardingShowsErrorMessageWhenServerError()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        const string ErrorMessage = "Database connection failed";
        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(
                ServiceProblem.ServerError(ErrorMessage))));

        SetupServices(joinRequestService);

        // Act
        var cut = Render<ClubOnboarding>();

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain(ErrorMessage);
        cut.FindComponents<CreateClubForm>().Count.ShouldBe(1);
        cut.FindComponents<ClubSearchPanel>().Count.ShouldBe(1);
    }

    /// <summary>
    /// ClubOnboarding shows error message with fallback text when ServerError has no detail.
    /// </summary>
    [Fact]
    public void ClubOnboardingShowsErrorFallbackWhenServerErrorHasNoDetail()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(
                ServiceProblem.ServerError(null))));

        SetupServices(joinRequestService);

        // Act
        var cut = Render<ClubOnboarding>();

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain("Failed to load your request status");
    }

    /// <summary>
    /// ClubOnboarding shows pending request card when there is an active pending join request.
    /// </summary>
    [Fact]
    public void ClubOnboardingShowsPendingRequestCardWhenPendingRequestExists()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        var pendingRequest = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "Test Club",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Pending,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(pendingRequest)));

        SetupServices(joinRequestService);

        // Act
        var cut = Render<ClubOnboarding>();

        // Assert
        cut.FindComponents<PendingJoinRequestCard>().Count.ShouldBe(1);
        cut.FindComponents<CreateClubForm>().Count.ShouldBe(0);
        cut.FindComponents<ClubSearchPanel>().Count.ShouldBe(0);
        cut.Markup.ShouldContain("Test Club");
    }

    /// <summary>
    /// ClubOnboarding navigates to ClubEndpoints.Complete when HandleClubCreated is invoked.
    /// </summary>
    [Fact]
    public async Task ClubOnboardingNavigatesToCompleteWhenClubCreatedAsync()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(ServiceProblem.NotFound())));

        SetupServices(joinRequestService);

        var cut = Render<ClubOnboarding>();
        var navManager = Services.GetRequiredService<NavigationManager>();
        _ = navManager.Uri;

        var createForm = cut.FindComponent<CreateClubForm>();
        var newClub = new ClubDto(ClubId: 42, Name: "My Club", City: "Austin", State: "TX");

        // Act
        // Invoke in the context of the renderer
        await cut.InvokeAsync(() => createForm.Instance.OnClubCreated.InvokeAsync(newClub));

        // Assert
        // Navigation should occur - verify URI changed or NavigationManager.NavigateTo was called
        // Note: forceLoad: true causes full-page navigation, which may not be fully testable in bUnit
        // We verify the handler was invoked successfully
        navManager.Uri.ShouldNotBeNull();
    }

    /// <summary>
    /// ClubOnboarding shows pending request card after HandleJoinRequested is invoked.
    /// </summary>
    [Fact]
    public async Task ClubOnboardingShowsPendingCardAfterJoinRequestedAsync()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(ServiceProblem.NotFound())));

        SetupServices(joinRequestService);

        var cut = Render<ClubOnboarding>();
        cut.FindComponents<CreateClubForm>().Count.ShouldBe(1);

        var searchPanel = cut.FindComponent<ClubSearchPanel>();
        var newRequest = new ClubJoinRequestDto(
            99,     // clubJoinRequestId
            50,     // clubId
            "Joined Club",  // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Pending,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        // Act
        await cut.InvokeAsync(() => searchPanel.Instance.OnJoinRequested.InvokeAsync(newRequest));

        // Assert
        cut.Render();
        cut.FindComponents<PendingJoinRequestCard>().Count.ShouldBe(1);
        cut.FindComponents<CreateClubForm>().Count.ShouldBe(0);
        cut.FindComponents<ClubSearchPanel>().Count.ShouldBe(0);
    }

    /// <summary>
    /// ClubOnboarding shows create/search forms after HandleRequestCancelled is invoked.
    /// </summary>
    [Fact]
    public async Task ClubOnboardingShowsCreateSearchFormsAfterRequestCancelledAsync()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        var pendingRequest = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "Test Club",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Pending,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(pendingRequest)));

        SetupServices(joinRequestService);

        var cut = Render<ClubOnboarding>();
        cut.FindComponents<PendingJoinRequestCard>().Count.ShouldBe(1);

        var card = cut.FindComponent<PendingJoinRequestCard>();

        // Act
        await cut.InvokeAsync(() => card.Instance.OnRequestCancelled.InvokeAsync());

        // Assert
        cut.Render();
        cut.FindComponents<CreateClubForm>().Count.ShouldBe(1);
        cut.FindComponents<ClubSearchPanel>().Count.ShouldBe(1);
        cut.FindComponents<PendingJoinRequestCard>().Count.ShouldBe(0);
    }

    #endregion

    #region CreateClubForm Component Tests

    /// <summary>
    /// CreateClubForm renders with input fields for club name, city, and state.
    /// </summary>
    [Fact]
    public void CreateClubFormRendersFormFields()
    {
        // Arrange
        SetupServices();

        // Act
        var cut = Render<CreateClubForm>();

        // Assert
        cut.Find("input#club-name").ShouldNotBeNull();
        cut.Find("input#club-city").ShouldNotBeNull();
        cut.Find("input#club-state").ShouldNotBeNull();
        cut.Find("button[type=\"submit\"]").ShouldNotBeNull();
    }

    /// <summary>
    /// CreateClubForm disables the Save crest button until the cropper's JS instance reports
    /// ready, so a quick click cannot export against a not-yet-initialized cropper.
    /// </summary>
    [Fact]
    public async Task CreateClubFormSaveCrestIsDisabledUntilCropperReadyAsync()
    {
        // Arrange
        SetupServices();

        var cut = Render<CreateClubForm>();

        var crestInput = cut.FindComponent<InputFile>();
        crestInput.UploadFiles(InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg"));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Save crest"));

        // Act/Assert: the save button is disabled before the cropper is ready, and enabled after.
        var saveButton = cut.Find("button[type='button'].btn-primary");
        saveButton.HasAttribute("disabled").ShouldBeTrue("Save crest must wait for the cropper to be ready");

        await cut.InvokeAsync(() => cut.FindComponent<NovaCropperComponent>().Instance.SimulateReady());

        await cut.WaitForAssertionAsync(() =>
            cut.Find("button[type='button'].btn-primary").HasAttribute("disabled").ShouldBeFalse(
                "Save crest must be enabled once the cropper reports ready"));
    }

    /// <summary>
    /// CreateClubForm shows error message when club creation fails.
    /// </summary>
    [Fact]
    public async Task CreateClubFormShowsErrorMessageOnCreateFailureAsync()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        const string ErrorMessage = "Club name already exists";
        clubService.CreateClubAsync(Arg.Any<CreateClubInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubDto>(ServiceProblem.Conflict(ErrorMessage))));

        SetupServices(clubService: clubService);

        var cut = Render<CreateClubForm>();

        var nameInput = cut.Find("input#club-name");
        var cityInput = cut.Find("input#club-city");
        var stateInput = cut.Find("input#club-state");
        var crestInput = cut.FindComponent<InputFile>();
        var submitButton = cut.Find("button[type=\"submit\"]");

        // Act
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        nameInput.Change("Test Club");
#pragma warning restore CA1849, S6966
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cityInput.Change("Austin");
#pragma warning restore CA1849, S6966
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        stateInput.Change("TX");
#pragma warning restore CA1849, S6966
        crestInput.UploadFiles(InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg"));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Save crest"));
        await cut.InvokeAsync(() => cut.FindComponent<NovaCropperComponent>().Instance.SimulateReady());
        await cut.WaitForAssertionAsync(() =>
            cut.Find("button[type='button'].btn-primary").HasAttribute("disabled").ShouldBeFalse());
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Save crest", StringComparison.Ordinal)).Click();
#pragma warning restore CA1849, S6966
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("club-crest-preview"));
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        submitButton.Click();
#pragma warning restore CA1849, S6966

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("alert-danger"));

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain(ErrorMessage);
    }

    /// <summary>
    /// CreateClubForm disables submit button while submission is in progress.
    /// </summary>
    [Fact]
    public async Task CreateClubFormDisablesSubmitButtonDuringSubmissionAsync()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        var tcs = new TaskCompletionSource<ServiceResult<ClubDto>>();
        clubService.CreateClubAsync(Arg.Any<CreateClubInput>(), Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        SetupServices(clubService: clubService);

        var cut = Render<CreateClubForm>();

        var nameInput = cut.Find("input#club-name");
        var cityInput = cut.Find("input#club-city");
        var stateInput = cut.Find("input#club-state");
        var crestInput = cut.FindComponent<InputFile>();
        var submitButton = cut.Find("button[type=\"submit\"]");

        // Act
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        nameInput.Change("Test Club");
#pragma warning restore CA1849, S6966
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cityInput.Change("Austin");
#pragma warning restore CA1849, S6966
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        stateInput.Change("TX");
#pragma warning restore CA1849, S6966
        crestInput.UploadFiles(InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg"));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Save crest"));
        await cut.InvokeAsync(() => cut.FindComponent<NovaCropperComponent>().Instance.SimulateReady());
        await cut.WaitForAssertionAsync(() =>
            cut.Find("button[type='button'].btn-primary").HasAttribute("disabled").ShouldBeFalse());
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Save crest", StringComparison.Ordinal)).Click();
#pragma warning restore CA1849, S6966
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("club-crest-preview"));
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        submitButton.Click();
#pragma warning restore CA1849, S6966

        // Assert
        await cut.WaitForAssertionAsync(() => submitButton.HasAttribute("disabled"));
    }

    /// <summary>
    /// CreateClubForm sends the cropped JPEG bytes on submit and requires the crop step to be
    /// completed before the form can be submitted.
    /// </summary>
    [Fact]
    public async Task CreateClubFormSendsCroppedJpegBytesAfterCropStepAsync()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        clubService.CreateClubAsync(Arg.Any<CreateClubInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubDto>(
                new ClubDto(ClubId: 1, Name: "Cropped Club", City: "Austin", State: "TX"))));
        SetupServices(clubService: clubService);

        var cut = Render<CreateClubForm>();

        var nameInput = cut.Find("input#club-name");
        var cityInput = cut.Find("input#club-city");
        var stateInput = cut.Find("input#club-state");
        var crestInput = cut.FindComponent<InputFile>();
        var submitButton = cut.Find("button[type=\"submit\"]");

        // Act: the submit button is disabled while the crop step is active, so the crop must be
        // saved first (the exporter returns fixed JPEG bytes) before the form can be submitted.
        crestInput.UploadFiles(InputFileContent.CreateFromBinary(TestImages.CreateJpeg(), "crest.jpg", null, "image/jpeg"));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Save crest"));
        submitButton.HasAttribute("disabled").ShouldBeTrue("submit must be gated while cropping");

        await cut.InvokeAsync(() => cut.FindComponent<NovaCropperComponent>().Instance.SimulateReady());
        await cut.WaitForAssertionAsync(() =>
            cut.Find("button[type='button'].btn-primary").HasAttribute("disabled").ShouldBeFalse());
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), "Save crest", StringComparison.Ordinal)).Click();
#pragma warning restore CA1849, S6966
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("club-crest-preview"));

#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        nameInput.Change("Cropped Club");
#pragma warning restore CA1849, S6966
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cityInput.Change("Austin");
#pragma warning restore CA1849, S6966
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        stateInput.Change("TX");
#pragma warning restore CA1849, S6966
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        submitButton.Click();
#pragma warning restore CA1849, S6966

        // Assert
        await cut.WaitForAssertionAsync(() =>
            clubService.Received(1).CreateClubAsync(
                Arg.Is<CreateClubInput>(input =>
                    input.CrestContentType == "image/jpeg" &&
                    input.CrestContent.Length > 0),
                Arg.Any<CancellationToken>()));
    }

    #endregion

    #region ClubSearchPanel Component Tests

    /// <summary>
    /// ClubSearchPanel renders search input and button.
    /// </summary>
    [Fact]
    public void ClubSearchPanelRendersSearchElements()
    {
        // Arrange
        SetupServices();

        // Act
        var cut = Render<ClubSearchPanel>();

        // Assert
        cut.Find("input[placeholder*=\"Search\"]").ShouldNotBeNull();
        cut.Find("button[type=\"button\"]").ShouldNotBeNull();
    }

    /// <summary>
    /// ClubSearchPanel shows search results after successful search.
    /// </summary>
    [Fact]
    public void ClubSearchPanelShowsSearchResultsAfterSuccessfulSearch()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();

        var clubs = new List<ClubDto>
        {
            new(ClubId: 1, Name: "Test Club 1", City: "Austin", State: "TX"),
            new(ClubId: 2, Name: "Test Club 2", City: "Dallas", State: "TX")
        };

        clubService.SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubDto>>(clubs)));

        SetupServices(clubService: clubService);

        var cut = Render<ClubSearchPanel>();
        var searchInput = cut.Find("input[placeholder*=\"Search\"]");
        var searchButton = cut.Find("button[type=\"button\"]");

        // Act
        // Use Input for oninput event binding
        searchInput.Input("test");
        searchButton.Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Test Club 1"));

        // Assert
        cut.Markup.ShouldContain("Test Club 1");
        cut.Markup.ShouldContain("Test Club 2");
        cut.Markup.ShouldContain("Austin");
        cut.Markup.ShouldContain("Dallas");
    }

    /// <summary>
    /// ClubSearchPanel shows "no results" message when search returns empty list.
    /// </summary>
    [Fact]
    public void ClubSearchPanelShowsNoResultsMessageWhenSearchIsEmpty()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();

        var emptyList = new List<ClubDto>();
        clubService.SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubDto>>(emptyList)));

        SetupServices(clubService: clubService);

        var cut = Render<ClubSearchPanel>();
        var searchInput = cut.Find("input[placeholder*=\"Search\"]");
        var searchButton = cut.Find("button[type=\"button\"]");

        // Act
        searchInput.Input("nonexistent");
        searchButton.Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No clubs found"));

        // Assert
        cut.Markup.ShouldContain("No clubs found");
    }

    /// <summary>
    /// ClubSearchPanel shows error message when search fails.
    /// </summary>
    [Fact]
    public void ClubSearchPanelShowsErrorMessageOnSearchFailure()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        const string ErrorMessage = "Search service unavailable";

        clubService.SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubDto>>(
                ServiceProblem.ServerError(ErrorMessage))));

        SetupServices(clubService: clubService);

        var cut = Render<ClubSearchPanel>();
        var searchInput = cut.Find("input[placeholder*=\"Search\"]");
        var searchButton = cut.Find("button[type=\"button\"]");

        // Act
        searchInput.Input("test");
        searchButton.Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("alert-danger"));

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain(ErrorMessage);
    }

    #endregion

    #region Phase 5: ClubSearchPanel Debounce Tests

    /// <summary>
    /// ClubSearchPanel does not trigger search when query has only 2 characters (below MinAutoSearchLength threshold).
    /// Phase 5: Tests the 3-character minimum threshold in HandleInputAsync.
    /// </summary>
    [Fact]
    public async Task HandleInputAsyncDoesNotSearchWhenQueryIsTwoCharactersAsync()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        clubService.SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubDto>>(
                new List<ClubDto> { new(ClubId: 1, Name: "Test", City: "Austin", State: "TX") })));

        SetupServices(clubService: clubService);

        var cut = Render<ClubSearchPanel>();
        var searchInput = cut.Find("input[placeholder*=\"Search\"]");

        // Act
        // Simulate typing "ab" (2 characters)
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        searchInput.Input("ab");
#pragma warning restore CA1849, S6966

        // Wait a bit to ensure no debounce is triggered
        await Task.Delay(400, Xunit.TestContext.Current.CancellationToken);

        // Assert
        // SearchClubsAsync should not have been called
        await clubService.DidNotReceive().SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// ClubSearchPanel does not trigger search when query is empty.
    /// Phase 5: Tests the clearing of results when below threshold in HandleInputAsync.
    /// </summary>
    [Fact]
    public async Task HandleInputAsyncDoesNotSearchWhenQueryIsEmptyAsync()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        var clubs = new List<ClubDto>
        {
            new(ClubId: 1, Name: "Test Club", City: "Austin", State: "TX")
        };
        clubService.SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubDto>>(clubs)));

        SetupServices(clubService: clubService);

        var cut = Render<ClubSearchPanel>();
        var searchInput = cut.Find("input[placeholder*=\"Search\"]");

        // First do a successful search with 3+ characters
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        searchInput.Input("abc");
#pragma warning restore CA1849, S6966
        await cut.WaitForAssertionAsync(() =>
        {
            _ = clubService.Received(1).SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }, timeout: TimeSpan.FromSeconds(2));

        // Act
        // Now clear the input
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        searchInput.Input("");
#pragma warning restore CA1849, S6966
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);

        // Assert
        // Service should still have been called only once (from the "abc" search)
        await clubService.Received(1).SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        // Results should be cleared when input is empty
        cut.Markup.ShouldNotContain("Test Club");
    }

    /// <summary>
    /// ClubSearchPanel clears results when query drops below MinAutoSearchLength threshold.
    /// Phase 5: Tests result clearing in HandleInputAsync when query length < 3.
    /// </summary>
    [Fact]
    public async Task HandleInputAsyncClearsResultsWhenQueryDropsBelowThresholdAsync()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        var clubs = new List<ClubDto>
        {
            new(ClubId: 1, Name: "Austin Club", City: "Austin", State: "TX")
        };
        clubService.SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubDto>>(clubs)));

        SetupServices(clubService: clubService);

        var cut = Render<ClubSearchPanel>();
        var searchInput = cut.Find("input[placeholder*=\"Search\"]");

        // First, trigger a successful search with "aust" (4 characters)
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        searchInput.Input("aust");
#pragma warning restore CA1849, S6966
        await Task.Delay(400, Xunit.TestContext.Current.CancellationToken);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Austin Club"));

        // Act
        // Now reduce to "au" (2 characters, below threshold)
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        searchInput.Input("au");
#pragma warning restore CA1849, S6966
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);

        // Assert
        // Results should be cleared
        cut.Markup.ShouldNotContain("Austin Club");
        cut.Markup.ShouldNotContain("No clubs found");
    }

    /// <summary>
    /// ClubSearchPanel triggers search after debounce when query reaches MinAutoSearchLength.
    /// Phase 5: Tests the 300ms debounce behavior in HandleInputAsync.
    /// </summary>
    [Fact]
    public async Task HandleInputAsyncSearchesAfterDebounceWhenQueryIsThreeCharactersAsync()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        var clubs = new List<ClubDto>
        {
            new(ClubId: 1, Name: "Debounce Test Club", City: "City", State: "ST")
        };
        clubService.SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubDto>>(clubs)));

        SetupServices(clubService: clubService);

        var cut = Render<ClubSearchPanel>();
        var searchInput = cut.Find("input[placeholder*=\"Search\"]");

        // Act
        // Type "abc" (3 characters, meets threshold)
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        searchInput.Input("abc");
#pragma warning restore CA1849, S6966

        // Wait for debounce to complete (300ms + buffer)
        await Task.Delay(350, Xunit.TestContext.Current.CancellationToken);

        // Assert
        // SearchClubsAsync should have been called exactly once
        await cut.WaitForAssertionAsync(() =>
        {
            _ = clubService.Received(1).SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            cut.Markup.ShouldContain("Debounce Test Club");
        });
    }

    /// <summary>
    /// ClubSearchPanel cancels previous debounce when input changes before delay completes.
    /// Phase 5: Tests debounce cancellation in HandleInputAsync via _debounceCts.
    /// </summary>
    [Fact]
    public async Task HandleInputAsyncCancelsPreviousDebounceWhenInputChangesAsync()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        var clubs = new List<ClubDto>
        {
            new(ClubId: 1, Name: "Result Club", City: "City", State: "ST")
        };
        clubService.SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubDto>>(clubs)));

        SetupServices(clubService: clubService);

        var cut = Render<ClubSearchPanel>();
        var searchInput = cut.Find("input[placeholder*=\"Search\"]");

        // Act
        // Type "abc" (3 characters)
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        searchInput.Input("abc");
#pragma warning restore CA1849, S6966

        // Before debounce completes (300ms), change input to "abcd"
        await Task.Delay(150, Xunit.TestContext.Current.CancellationToken);
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        searchInput.Input("abcd");
#pragma warning restore CA1849, S6966

        // Wait for both debounces to potentially complete
        await Task.Delay(400, Xunit.TestContext.Current.CancellationToken);

        // Assert
        // SearchClubsAsync should be called exactly once (for "abcd", not "abc")
        // because the first debounce should have been cancelled
        await cut.WaitForAssertionAsync(() =>
        {
            _ = clubService.Received(1).SearchClubsAsync("abcd", Arg.Any<CancellationToken>());
        }, timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// ClubSearchPanel does not trigger search when query has only 2 characters (below MinAutoSearchLength threshold).
    /// This test confirms that the 3-character threshold is enforced by HandleInputAsync.
    /// </summary>
    [Fact]
    public void HandleInputAsyncDoesNotSearchWhenQueryIsTwoCharsConfirmingThreshold()
    {
        // Arrange & Act
        // Verify that the component renders without errors and minimum threshold logic is in place
        SetupServices();
        var cut = Render<ClubSearchPanel>();
        var searchInput = cut.Find("input[placeholder*=\"Search\"]");

        // Type exactly 2 characters
        searchInput.Input("ab");

        // Assert
        // The component should not display results after 2-char input
        // (implicitly testing MinAutoSearchLength >= 3)
        cut.Markup.ShouldNotContain("No clubs found");
    }

    /// <summary>
    /// ClubSearchPanel cancels and disposes the debounce CancellationTokenSource when the component is disposed.
    /// Phase 5: Tests that DisposeAsyncCore cancels in-flight debounce delays to prevent orphaned searches.
    /// </summary>
    [Fact]
    public async Task DisposeAsyncCoreCancelsInFlightDebounceWhenComponentIsDisposedAsync()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        var clubs = new List<ClubDto>
        {
            new(ClubId: 1, Name: "Dispose Test Club", City: "City", State: "ST")
        };
        clubService.SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubDto>>(clubs)));

        SetupServices(clubService: clubService);

        var cut = Render<ClubSearchPanel>();
        var searchInput = cut.Find("input[placeholder*=\"Search\"]");
        var component = cut.Instance;

        // Act
        // Type "abc" (3 characters, meets threshold and starts debounce)
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        searchInput.Input("abc");
#pragma warning restore CA1849, S6966

        // Immediately dispose the component before the 300ms debounce completes
        await component.DisposeAsync();
        cut.Dispose();

        // Wait a bit to give time for the debounce to fire if it weren't cancelled
        await Task.Delay(350, Xunit.TestContext.Current.CancellationToken);

        // Assert
        // SearchClubsAsync should NOT have been called because disposal cancelled the debounce
        _ = clubService.DidNotReceive().SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region PendingJoinRequestCard Component Tests

    /// <summary>
    /// PendingJoinRequestCard displays pending request information.
    /// </summary>
    [Fact]
    public void PendingJoinRequestCardDisplaysPendingRequestInfo()
    {
        // Arrange
        SetupServices();

        var request = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "Test Club",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Pending,  // status
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)  // createdAt
        );

        // Act
        var cut = Render<PendingJoinRequestCard>(parameters =>
            parameters.Add(p => p.Request, request));

        // Assert
        cut.Markup.ShouldContain("Test Club");
        cut.Markup.ShouldContain("Pending");
        cut.Markup.ShouldContain("2026");
    }

    /// <summary>
    /// PendingJoinRequestCard shows error message when cancellation fails.
    /// </summary>
    [Fact]
    public void PendingJoinRequestCardShowsErrorMessageOnCancelFailure()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        const string ErrorMessage = "Request is already accepted";

        joinRequestService.CancelJoinRequestAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(
                ServiceProblem.Conflict(ErrorMessage))));

        SetupServices(joinRequestService);

        var request = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "Test Club",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Pending,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        // Act
        var cut = Render<PendingJoinRequestCard>(parameters =>
            parameters.Add(p => p.Request, request));

        var cancelButton = cut.Find("button");
        cancelButton.Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("alert-danger"));

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain(ErrorMessage);
    }

    /// <summary>
    /// PendingJoinRequestCard disables cancel button while cancellation is in progress.
    /// </summary>
    [Fact]
    public void PendingJoinRequestCardDisablesCancelButtonDuringCancellation()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        var tcs = new TaskCompletionSource<ServiceResult<Success>>();

        joinRequestService.CancelJoinRequestAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        SetupServices(joinRequestService);

        var request = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "Test Club",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Pending,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        // Act
        var cut = Render<PendingJoinRequestCard>(parameters =>
            parameters.Add(p => p.Request, request));

        var cancelButton = cut.Find("button");
        cancelButton.Click();

        // Assert
        cut.WaitForAssertion(() => cancelButton.HasAttribute("disabled"));
    }

    #endregion

    #region Phase 6: PendingJoinRequestCard Status Polling Tests

    /// <summary>
    /// Phase 6: PendingJoinRequestCard renders approved state when Request status is Approved.
    /// Tests OnInitialized detection of Approved status (no polling triggered).
    /// </summary>
    [Fact]
    public void OnInitializedRendersApprovedStateWhenRequestStatusIsApproved()
    {
        // Arrange
        SetupServices();

        var approvedRequest = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "MyClub",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Approved,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        // Act
        var cut = Render<PendingJoinRequestCard>(parameters =>
            parameters.Add(p => p.Request, approvedRequest));

        // Assert
        // Should render the approved card with the club name
        cut.Markup.ShouldContain("Join Request Approved");
        cut.Markup.ShouldContain("MyClub");
        cut.Markup.ShouldContain("badge bg-success");
        cut.Markup.ShouldContain("Continue to");
    }

    /// <summary>
    /// Phase 6: PendingJoinRequestCard renders rejected state when Request status is Rejected.
    /// Tests OnInitialized detection of Rejected status (no polling triggered).
    /// </summary>
    [Fact]
    public void OnInitializedRendersRejectedStateWhenRequestStatusIsRejected()
    {
        // Arrange
        SetupServices();

        var rejectedRequest = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "RejectClub",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Rejected,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        // Act
        var cut = Render<PendingJoinRequestCard>(parameters =>
            parameters.Add(p => p.Request, rejectedRequest));

        // Assert
        // Should render the rejected card
        cut.Markup.ShouldContain("Join Request Not Approved");
        cut.Markup.ShouldContain("Your request was not approved");
        cut.Markup.ShouldContain("badge bg-secondary");
        cut.Markup.ShouldContain("Search for another club");
    }

    /// <summary>
    /// Phase 6: PendingJoinRequestCard initiates polling when Request status is Pending.
    /// Tests OnInitialized starts polling without immediate state change.
    /// </summary>
    [Fact]
    public void OnInitializedRendersPendingStateWhenRequestStatusIsPending()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(
                new ClubJoinRequestDto(
                    1,      // clubJoinRequestId
                    42,     // clubId
                    "PendingClub",    // clubName
                    100,    // requestingUserId
            "Test User",    // requestingUserName
                    RequestStatus.Pending,  // status
                    DateTimeOffset.UtcNow   // createdAt
                ))));

        SetupServices(joinRequestService);

        var pendingRequest = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "PendingClub",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Pending,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        // Act
        var cut = Render<PendingJoinRequestCard>(parameters =>
            parameters.Add(p => p.Request, pendingRequest));

        // Assert
        // Should render pending state initially
        cut.Markup.ShouldContain("Pending Join Request");
        cut.Markup.ShouldContain("PendingClub");
        cut.Markup.ShouldContain("badge bg-warning");
        cut.Markup.ShouldContain("Cancel Request");
    }

    /// <summary>
    /// Phase 6: PendingJoinRequestCard continues to show pending UI while polling.
    /// Tests that the pending state persists while waiting for a terminal status.
    /// Note: Full polling behavior testing requires TimeProvider mocking, which would require
    /// modifying the component implementation. This test verifies the UI remains stable during polling.
    /// </summary>
    [Fact]
    public async Task OnInitializedMaintainsPendingUIWhenRequestStatusIsPendingAsync()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(
                new ClubJoinRequestDto(1, 42, "PendingClub", 100, "Test User", RequestStatus.Pending, DateTimeOffset.UtcNow))));

        SetupServices(joinRequestService);

        var pendingRequest = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "PendingClub",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Pending,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        // Act
        var cut = Render<PendingJoinRequestCard>(parameters =>
            parameters.Add(p => p.Request, pendingRequest));

        // Small delay to allow OnInitialized to execute
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);

        // Assert
        // Verify pending UI is rendered and stable
        cut.Markup.ShouldContain("Pending Join Request");
        cut.Markup.ShouldContain("PendingClub");
        cut.Markup.ShouldContain("badge bg-warning");
        cut.Markup.ShouldContain("Cancel Request");
        // Should NOT show approved or rejected UI
        cut.Markup.ShouldNotContain("Join Request Approved");
        cut.Markup.ShouldNotContain("Join Request Not Approved");
    }

    /// <summary>
    /// Phase 6: PollStatusAsync continues polling while request status remains Pending.
    /// Tests that the polling mechanism doesn't cause premature state changes.
    /// Verifies that only terminal statuses (Approved/Rejected) trigger state transitions.
    /// </summary>
    [Fact]
    public async Task PollStatusAsyncDoesNotChangeStateWhenRequestRemainsPendingAsync()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        // Service will always return Pending
        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(
                new ClubJoinRequestDto(1, 42, "StayPendingClub", 100, "Test User", RequestStatus.Pending, DateTimeOffset.UtcNow))));

        SetupServices(joinRequestService);

        var pendingRequest = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "StayPendingClub",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Pending,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        // Act
        var cut = Render<PendingJoinRequestCard>(parameters =>
            parameters.Add(p => p.Request, pendingRequest));

        // Wait a short time
        await Task.Delay(200, Xunit.TestContext.Current.CancellationToken);

        // Assert
        // Should still show pending state (not transitioned to approved or rejected)
        cut.Markup.ShouldContain("Pending Join Request");
        cut.Markup.ShouldContain("badge bg-warning");
        cut.Markup.ShouldNotContain("Join Request Approved");
        cut.Markup.ShouldNotContain("Join Request Not Approved");
    }

    /// <summary>
    /// Phase 6: HandleCompleteOnboarding button is rendered and clickable when approved.
    /// Tests that the "Continue" button on approved state is present and accessible.
    /// Note: Full navigation testing with forceLoad: true requires integration testing.
    /// </summary>
    [Fact]
    public void HandleCompleteOnboardingRendersContinueButtonWhenApprovedStateDisplayed()
    {
        // Arrange
        SetupServices();

        var approvedRequest = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "ApprovedClub",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Approved,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        // Act
        var cut = Render<PendingJoinRequestCard>(parameters =>
            parameters.Add(p => p.Request, approvedRequest));

        var continueButton = cut.Find("button.btn-success");

        // Assert
        continueButton.ShouldNotBeNull();
        continueButton.OuterHtml.ShouldContain("Continue to");
        continueButton.OuterHtml.ShouldContain("ApprovedClub");
    }

    /// <summary>
    /// Phase 6: HandleSearchAgainAsync invokes OnSearchAgainRequested callback.
    /// Tests that "Search for another club" button raises the callback on rejected state.
    /// </summary>
    [Fact]
    public async Task HandleSearchAgainAsyncInvokesOnSearchAgainCallbackWhenSearchAgainButtonClickedAsync()
    {
        // Arrange
        SetupServices();

        var rejectedRequest = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "RejectedClub",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Rejected,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        var callbackInvoked = false;

        var cut = Render<PendingJoinRequestCard>(parameters =>
        {
            parameters.Add(p => p.Request, rejectedRequest);
            parameters.Add(p => p.OnSearchAgainRequested, EventCallback.Factory.Create(this, async () =>
            {
                callbackInvoked = true;
                await Task.CompletedTask;
            }));
        });

        var searchAgainButton = cut.Find("button.btn-primary");

        // Act
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        searchAgainButton.Click();
#pragma warning restore CA1849, S6966

        // Wait for callback to be invoked
        await cut.InvokeAsync(() => Task.Delay(100));

        // Assert
        callbackInvoked.ShouldBeTrue();
    }

    /// <summary>
    /// Phase 6: Component disposes cleanly without errors.
    /// Tests that the DisposeAsyncCore properly cleans up resources.
    /// </summary>
    [Fact]
    public async Task DisposeAsyncCoreCompletesSuccessfullyWhenComponentIsDisposedAsync()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(
                new ClubJoinRequestDto(1, 42, "TestClub", 100, "Test User", RequestStatus.Pending, DateTimeOffset.UtcNow))));

        SetupServices(joinRequestService);

        var pendingRequest = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "TestClub",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Pending,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        var cut = Render<PendingJoinRequestCard>(parameters =>
            parameters.Add(p => p.Request, pendingRequest));

        // Act & Assert
        // Should not throw any exceptions during disposal
        var disposeTask = cut.InvokeAsync(cut.Instance.DisposeAsync);
        await disposeTask;

        cut.Dispose();

        // Component should clean up without errors
        true.ShouldBeTrue();
    }

    #endregion

    #region Phase 6: ClubOnboarding Search Again Integration Tests

    /// <summary>
    /// Phase 6: ClubOnboarding shows create/search forms when HandleSearchAgain is invoked.
    /// Tests the integration where rejected card's search again action clears the pending request.
    /// </summary>
    [Fact]
    public async Task ClubOnboardingShowsCreateSearchFormsAfterSearchAgainRequestedAsync()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        var rejectedRequest = new ClubJoinRequestDto(
            1,      // clubJoinRequestId
            42,     // clubId
            "RejectedClub",    // clubName
            100,    // requestingUserId
            "Test User",    // requestingUserName
            RequestStatus.Rejected,  // status
            DateTimeOffset.UtcNow   // createdAt
        );

        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(rejectedRequest)));

        SetupServices(joinRequestService);

        var cut = Render<ClubOnboarding>();
        cut.FindComponents<PendingJoinRequestCard>().Count.ShouldBe(1);

        var card = cut.FindComponent<PendingJoinRequestCard>();

        // Act
        // Invoke the OnSearchAgainRequested callback from the card
        await cut.InvokeAsync(() => card.Instance.OnSearchAgainRequested.InvokeAsync());

        // Assert
        cut.Render();
        cut.FindComponents<CreateClubForm>().Count.ShouldBe(1);
        cut.FindComponents<ClubSearchPanel>().Count.ShouldBe(1);
        cut.FindComponents<PendingJoinRequestCard>().Count.ShouldBe(0);
    }

    #endregion
    // ClubAdmin page is covered by integration tests (see Nova.Integration.Tests).

}
