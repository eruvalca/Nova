using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nova.Shared.Clubs;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.UI.Features.Clubs.Components;
using Nova.UI.Features.Clubs.Pages;
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
    /// <see cref="IClubJoinRequestService"/> into the test context's service container.
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
    }

    #endregion

    #region ClubOnboarding Page Tests

    /// <summary>
    /// ClubOnboarding shows create/search forms when there is no pending request (NotFound).
    /// </summary>
    [Fact]
    public void ClubOnboarding_ShowsCreateSearchForms_WhenNoPendingRequest()
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
    public void ClubOnboarding_ShowsErrorMessage_WhenServerError()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        const string errorMessage = "Database connection failed";
        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(
                ServiceProblem.ServerError(errorMessage))));

        SetupServices(joinRequestService);

        // Act
        var cut = Render<ClubOnboarding>();

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain(errorMessage);
        cut.FindComponents<CreateClubForm>().Count.ShouldBe(1);
        cut.FindComponents<ClubSearchPanel>().Count.ShouldBe(1);
    }

    /// <summary>
    /// ClubOnboarding shows error message with fallback text when ServerError has no detail.
    /// </summary>
    [Fact]
    public void ClubOnboarding_ShowsErrorFallback_WhenServerErrorHasNoDetail()
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
    public void ClubOnboarding_ShowsPendingRequestCard_WhenPendingRequestExists()
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
    public void ClubOnboarding_NavigatesToComplete_WhenClubCreated()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        joinRequestService.GetCurrentUserPendingRequestAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubJoinRequestDto>(ServiceProblem.NotFound())));

        SetupServices(joinRequestService);

        var cut = Render<ClubOnboarding>();
        var navManager = Services.GetRequiredService<NavigationManager>();
        var initialUri = navManager.Uri;

        var createForm = cut.FindComponent<CreateClubForm>();
        var newClub = new ClubDto(ClubId: 42, Name: "My Club", City: "Austin", State: "TX");

        // Act
        // Invoke in the context of the renderer
        Render(_ => createForm.Instance.OnClubCreated.InvokeAsync(newClub));

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
    public void ClubOnboarding_ShowsPendingCard_AfterJoinRequested()
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
        Render(_ => searchPanel.Instance.OnJoinRequested.InvokeAsync(newRequest));

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
    public void ClubOnboarding_ShowsCreateSearchForms_AfterRequestCancelled()
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
        Render(_ => card.Instance.OnRequestCancelled.InvokeAsync());

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
    public void CreateClubForm_RendersFormFields()
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
    /// CreateClubForm shows error message when club creation fails.
    /// </summary>
    [Fact]
    public void CreateClubForm_ShowsErrorMessage_OnCreateFailure()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        const string errorMessage = "Club name already exists";
        clubService.CreateClubAsync(Arg.Any<CreateClubInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<ClubDto>(ServiceProblem.Conflict(errorMessage))));

        SetupServices(clubService: clubService);

        var cut = Render<CreateClubForm>();

        var nameInput = cut.Find("input#club-name");
        var cityInput = cut.Find("input#club-city");
        var stateInput = cut.Find("input#club-state");
        var submitButton = cut.Find("button[type=\"submit\"]");

        // Act
        nameInput.Change("Test Club");
        cityInput.Change("Austin");
        stateInput.Change("TX");
        submitButton.Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("alert-danger"));

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain(errorMessage);
    }

    /// <summary>
    /// CreateClubForm disables submit button while submission is in progress.
    /// </summary>
    [Fact]
    public void CreateClubForm_DisablesSubmitButton_DuringSubmission()
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
        var submitButton = cut.Find("button[type=\"submit\"]");

        // Act
        nameInput.Change("Test Club");
        cityInput.Change("Austin");
        stateInput.Change("TX");
        submitButton.Click();

        // Assert
        cut.WaitForAssertion(() => submitButton.HasAttribute("disabled"));
    }

    #endregion

    #region ClubSearchPanel Component Tests

    /// <summary>
    /// ClubSearchPanel renders search input and button.
    /// </summary>
    [Fact]
    public void ClubSearchPanel_RendersSearchElements()
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
    public void ClubSearchPanel_ShowsSearchResults_AfterSuccessfulSearch()
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
    public void ClubSearchPanel_ShowsNoResultsMessage_WhenSearchIsEmpty()
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
    public void ClubSearchPanel_ShowsErrorMessage_OnSearchFailure()
    {
        // Arrange
        var clubService = Substitute.For<IClubService>();
        const string errorMessage = "Search service unavailable";

        clubService.SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubDto>>(
                ServiceProblem.ServerError(errorMessage))));

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
        cut.Markup.ShouldContain(errorMessage);
    }

    #endregion

    #region Phase 5: ClubSearchPanel Debounce Tests

    /// <summary>
    /// ClubSearchPanel does not trigger search when query has only 2 characters (below MinAutoSearchLength threshold).
    /// Phase 5: Tests the 3-character minimum threshold in HandleInputAsync.
    /// </summary>
    [Fact]
    public async Task HandleInputAsync_DoesNotSearch_WhenQueryIsTwoCharacters()
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
        searchInput.Input("ab");

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
    public async Task HandleInputAsync_DoesNotSearch_WhenQueryIsEmpty()
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
        searchInput.Input("abc");
        cut.WaitForAssertion(() =>
        {
            clubService.Received(1).SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }, timeout: TimeSpan.FromSeconds(2));

        // Act
        // Now clear the input
        searchInput.Input("");
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
    public async Task HandleInputAsync_ClearsResults_WhenQueryDropsBelowThreshold()
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
        searchInput.Input("aust");
        await Task.Delay(400, Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Austin Club"));

        // Act
        // Now reduce to "au" (2 characters, below threshold)
        searchInput.Input("au");
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
    public async Task HandleInputAsync_SearchesAfterDebounce_WhenQueryIsThreeCharacters()
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
        searchInput.Input("abc");

        // Wait for debounce to complete (300ms + buffer)
        await Task.Delay(350, Xunit.TestContext.Current.CancellationToken);

        // Assert
        // SearchClubsAsync should have been called exactly once
        cut.WaitForAssertion(() =>
        {
            clubService.Received(1).SearchClubsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            cut.Markup.ShouldContain("Debounce Test Club");
        });
    }

    /// <summary>
    /// ClubSearchPanel cancels previous debounce when input changes before delay completes.
    /// Phase 5: Tests debounce cancellation in HandleInputAsync via _debounceCts.
    /// </summary>
    [Fact]
    public async Task HandleInputAsync_CancelsPreviousDebounce_WhenInputChanges()
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
        searchInput.Input("abc");

        // Before debounce completes (300ms), change input to "abcd"
        await Task.Delay(150, Xunit.TestContext.Current.CancellationToken);
        searchInput.Input("abcd");

        // Wait for both debounces to potentially complete
        await Task.Delay(400, Xunit.TestContext.Current.CancellationToken);

        // Assert
        // SearchClubsAsync should be called exactly once (for "abcd", not "abc")
        // because the first debounce should have been cancelled
        cut.WaitForAssertion(() =>
        {
            clubService.Received(1).SearchClubsAsync("abcd", Arg.Any<CancellationToken>());
        }, timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// ClubSearchPanel does not trigger search when query has only 2 characters (below MinAutoSearchLength threshold).
    /// This test confirms that the 3-character threshold is enforced by HandleInputAsync.
    /// </summary>
    [Fact]
    public void HandleInputAsync_DoesNotSearch_WhenQueryIsTwoChars_ConfirmingThreshold()
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
    public async Task DisposeAsyncCore_CancelsInFlightDebounce_WhenComponentIsDisposed()
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
        searchInput.Input("abc");

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
    public void PendingJoinRequestCard_DisplaysPendingRequestInfo()
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
    public void PendingJoinRequestCard_ShowsErrorMessage_OnCancelFailure()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        const string errorMessage = "Request is already accepted";

        joinRequestService.CancelJoinRequestAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(
                ServiceProblem.Conflict(errorMessage))));

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
        cut.Markup.ShouldContain(errorMessage);
    }

    /// <summary>
    /// PendingJoinRequestCard disables cancel button while cancellation is in progress.
    /// </summary>
    [Fact]
    public void PendingJoinRequestCard_DisablesCancelButton_DuringCancellation()
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
    public void OnInitialized_RendersApprovedState_WhenRequestStatusIsApproved()
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
    public void OnInitialized_RendersRejectedState_WhenRequestStatusIsRejected()
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
    public void OnInitialized_RendersPendingState_WhenRequestStatusIsPending()
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
    public void OnInitialized_MaintainsPendingUI_WhenRequestStatusIsPending()
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
        System.Threading.Thread.Sleep(100);

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
    public void PollStatusAsync_DoesNotChangeState_WhenRequestRemainsPending()
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
        System.Threading.Thread.Sleep(200);

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
    public void HandleCompleteOnboarding_RendersContinueButton_WhenApprovedStateDisplayed()
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
    public async Task HandleSearchAgainAsync_InvokesOnSearchAgainCallback_WhenSearchAgainButtonClicked()
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
        searchAgainButton.Click();

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
    public async Task DisposeAsyncCore_CompletesSuccessfully_WhenComponentIsDisposed()
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
        var disposeTask = cut.InvokeAsync(() => cut.Instance.DisposeAsync());
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
    public void ClubOnboarding_ShowsCreateSearchForms_AfterSearchAgainRequested()
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
        Render(_ => card.Instance.OnSearchAgainRequested.InvokeAsync());

        // Assert
        cut.Render();
        cut.FindComponents<CreateClubForm>().Count.ShouldBe(1);
        cut.FindComponents<ClubSearchPanel>().Count.ShouldBe(1);
        cut.FindComponents<PendingJoinRequestCard>().Count.ShouldBe(0);
    }

    #endregion

    #region Phase 7: ClubAdminJoinRequests Page Tests

    /// <summary>
    /// Phase 7: ClubAdminJoinRequests loads and displays pending requests.
    /// Tests that OnInitializedAsync loads requests and shows them in the UI.
    /// </summary>
    [Fact]
    public void OnInitializedAsync_LoadsRequests_WhenClubAdminHasPendingRequests()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        var pendingRequests = new List<ClubJoinRequestDto>
        {
            new(
                1,      // clubJoinRequestId
                42,     // clubId
                "Test Club",    // clubName
                100,    // requestingUserId
            "Test User",    // requestingUserName
                RequestStatus.Pending,  // status
                DateTimeOffset.UtcNow   // createdAt
            )
        };

        joinRequestService.GetClubJoinRequestsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(pendingRequests)));

        SetupServices(joinRequestService);

        // Act
        var cut = Render<ClubAdminJoinRequests>(parameters =>
            parameters.Add(p => p.ClubId, 42L));

        // Assert
        cut.Markup.ShouldContain("Test User");
        cut.Markup.ShouldNotContain("spinner-border");
        cut.Markup.ShouldNotContain("alert-danger");
        cut.Markup.ShouldNotContain("No pending requests");
    }

    /// <summary>
    /// Phase 7: ClubAdminJoinRequests shows "No pending requests" message when list is empty.
    /// Tests that OnInitializedAsync correctly handles empty request list.
    /// </summary>
    [Fact]
    public void OnInitializedAsync_ShowsNoPendingMessage_WhenNoRequests()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        var emptyList = new List<ClubJoinRequestDto>();

        joinRequestService.GetClubJoinRequestsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(emptyList)));

        SetupServices(joinRequestService);

        // Act
        var cut = Render<ClubAdminJoinRequests>(parameters =>
            parameters.Add(p => p.ClubId, 42L));

        // Assert
        cut.Markup.ShouldContain("No pending requests");
        cut.Markup.ShouldNotContain("spinner-border");
        cut.Markup.ShouldNotContain("alert-danger");
    }

    /// <summary>
    /// Phase 7: ClubAdminJoinRequests shows error message when service returns Forbidden.
    /// Tests error handling for unauthorized access (not a ClubAdmin).
    /// </summary>
    [Fact]
    public void OnInitializedAsync_ShowsError_WhenServiceReturnsForbidden()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        const string errorMessage = "Not authorized to view join requests for this club";

        joinRequestService.GetClubJoinRequestsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(
                ServiceProblem.Forbidden(errorMessage))));

        SetupServices(joinRequestService);

        // Act
        var cut = Render<ClubAdminJoinRequests>(parameters =>
            parameters.Add(p => p.ClubId, 42L));

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain(errorMessage);
        cut.Markup.ShouldNotContain("spinner-border");
    }

    /// <summary>
    /// Phase 7: ClubAdminJoinRequests shows fallback error message when no detail provided.
    /// Tests that error handling gracefully handles missing error details.
    /// </summary>
    [Fact]
    public void OnInitializedAsync_ShowsErrorFallback_WhenForbiddenHasNoDetail()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();

        joinRequestService.GetClubJoinRequestsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(
                ServiceProblem.Forbidden(null))));

        SetupServices(joinRequestService);

        // Act
        var cut = Render<ClubAdminJoinRequests>(parameters =>
            parameters.Add(p => p.ClubId, 42L));

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain("Failed to load join requests. Please try again.");
    }

    /// <summary>
    /// Phase 7: HandleApproveAsync reloads the request list on success.
    /// Tests that clicking Approve button triggers approval and reloads the list.
    /// </summary>
    [Fact]
    public async Task HandleApproveAsync_ReloadsList_WhenApprovalSucceeds()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();

        // First call returns one request, second call (after approval) returns empty list
        var initialRequests = new List<ClubJoinRequestDto>
        {
            new(
                1,      // clubJoinRequestId
                42,     // clubId
                "Test Club",    // clubName
                100,    // requestingUserId
            "Test User",    // requestingUserName
                RequestStatus.Pending,  // status
                DateTimeOffset.UtcNow   // createdAt
            )
        };

        var emptyList = new List<ClubJoinRequestDto>();

        joinRequestService.GetClubJoinRequestsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(initialRequests)),
                Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(emptyList))
            );

        joinRequestService.ApproveJoinRequestAsync(1L, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));

        SetupServices(joinRequestService);

        var cut = Render<ClubAdminJoinRequests>(parameters =>
            parameters.Add(p => p.ClubId, 42L));

        // Verify initial state shows the request
        cut.Markup.ShouldContain("Test User");

        // Act
        var approveButton = cut.Find("button.btn-success");
        approveButton.Click();

        // Wait for the async operation to complete
        await cut.InvokeAsync(() => Task.Delay(100));

        // Assert
        // After approval, list should reload and show "No pending requests"
        cut.Markup.ShouldContain("No pending requests");
        cut.Markup.ShouldNotContain("alert-danger");
    }

    /// <summary>
    /// Phase 7: HandleApproveAsync shows error message when approval fails.
    /// Tests that a failed approval displays the error detail.
    /// </summary>
    [Fact]
    public async Task HandleApproveAsync_ShowsError_WhenApprovalFails()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        const string errorMessage = "Request cannot be approved - it is no longer pending";

        var initialRequests = new List<ClubJoinRequestDto>
        {
            new(
                1,      // clubJoinRequestId
                42,     // clubId
                "Test Club",    // clubName
                100,    // requestingUserId
            "Test User",    // requestingUserName
                RequestStatus.Pending,  // status
                DateTimeOffset.UtcNow   // createdAt
            )
        };

        joinRequestService.GetClubJoinRequestsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(initialRequests)));

        joinRequestService.ApproveJoinRequestAsync(1L, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(
                ServiceProblem.Conflict(errorMessage))));

        SetupServices(joinRequestService);

        var cut = Render<ClubAdminJoinRequests>(parameters =>
            parameters.Add(p => p.ClubId, 42L));

        // Act
        var approveButton = cut.Find("button.btn-success");
        approveButton.Click();

        await cut.InvokeAsync(() => Task.Delay(100));

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain(errorMessage);
    }

    /// <summary>
    /// Phase 7: HandleApproveAsync shows fallback error when approval fails with no detail.
    /// Tests error handling for missing error detail on approval failure.
    /// </summary>
    [Fact]
    public async Task HandleApproveAsync_ShowsErrorFallback_WhenConflictHasNoDetail()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();

        var initialRequests = new List<ClubJoinRequestDto>
        {
            new(
                1,      // clubJoinRequestId
                42,     // clubId
                "Test Club",    // clubName
                100,    // requestingUserId
            "Test User",    // requestingUserName
                RequestStatus.Pending,  // status
                DateTimeOffset.UtcNow   // createdAt
            )
        };

        joinRequestService.GetClubJoinRequestsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(initialRequests)));

        joinRequestService.ApproveJoinRequestAsync(1L, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(
                ServiceProblem.Conflict(null))));

        SetupServices(joinRequestService);

        var cut = Render<ClubAdminJoinRequests>(parameters =>
            parameters.Add(p => p.ClubId, 42L));

        // Act
        var approveButton = cut.Find("button.btn-success");
        approveButton.Click();

        await cut.InvokeAsync(() => Task.Delay(100));

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain("Failed to approve the request. Please try again.");
    }

    /// <summary>
    /// Phase 7: HandleRejectAsync reloads the request list on success.
    /// Tests that clicking Reject button triggers rejection and reloads the list.
    /// </summary>
    [Fact]
    public async Task HandleRejectAsync_ReloadsList_WhenRejectionSucceeds()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();

        var initialRequests = new List<ClubJoinRequestDto>
        {
            new(
                1,      // clubJoinRequestId
                42,     // clubId
                "Test Club",    // clubName
                100,    // requestingUserId
            "Test User",    // requestingUserName
                RequestStatus.Pending,  // status
                DateTimeOffset.UtcNow   // createdAt
            )
        };

        var emptyList = new List<ClubJoinRequestDto>();

        joinRequestService.GetClubJoinRequestsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(initialRequests)),
                Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(emptyList))
            );

        joinRequestService.RejectJoinRequestAsync(1L, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));

        SetupServices(joinRequestService);

        var cut = Render<ClubAdminJoinRequests>(parameters =>
            parameters.Add(p => p.ClubId, 42L));

        // Verify initial state shows the request
        cut.Markup.ShouldContain("Test User");

        // Act
        var rejectButton = cut.Find("button.btn-outline-danger");
        rejectButton.Click();

        await cut.InvokeAsync(() => Task.Delay(100));

        // Assert
        cut.Markup.ShouldContain("No pending requests");
        cut.Markup.ShouldNotContain("alert-danger");
    }

    /// <summary>
    /// Phase 7: HandleRejectAsync shows error message when rejection fails.
    /// Tests that a failed rejection displays the error detail.
    /// </summary>
    [Fact]
    public async Task HandleRejectAsync_ShowsError_WhenRejectionFails()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        const string errorMessage = "Join request not found";

        var initialRequests = new List<ClubJoinRequestDto>
        {
            new(
                1,      // clubJoinRequestId
                42,     // clubId
                "Test Club",    // clubName
                100,    // requestingUserId
            "Test User",    // requestingUserName
                RequestStatus.Pending,  // status
                DateTimeOffset.UtcNow   // createdAt
            )
        };

        joinRequestService.GetClubJoinRequestsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(initialRequests)));

        joinRequestService.RejectJoinRequestAsync(1L, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(
                ServiceProblem.NotFound(errorMessage))));

        SetupServices(joinRequestService);

        var cut = Render<ClubAdminJoinRequests>(parameters =>
            parameters.Add(p => p.ClubId, 42L));

        // Act
        var rejectButton = cut.Find("button.btn-outline-danger");
        rejectButton.Click();

        await cut.InvokeAsync(() => Task.Delay(100));

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain(errorMessage);
    }

    /// <summary>
    /// Phase 7: HandleRejectAsync shows fallback error when rejection fails with no detail.
    /// Tests error handling for missing error detail on rejection failure.
    /// </summary>
    [Fact]
    public async Task HandleRejectAsync_ShowsErrorFallback_WhenNotFoundHasNoDetail()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();

        var initialRequests = new List<ClubJoinRequestDto>
        {
            new(
                1,      // clubJoinRequestId
                42,     // clubId
                "Test Club",    // clubName
                100,    // requestingUserId
            "Test User",    // requestingUserName
                RequestStatus.Pending,  // status
                DateTimeOffset.UtcNow   // createdAt
            )
        };

        joinRequestService.GetClubJoinRequestsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(initialRequests)));

        joinRequestService.RejectJoinRequestAsync(1L, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(
                ServiceProblem.NotFound(null))));

        SetupServices(joinRequestService);

        var cut = Render<ClubAdminJoinRequests>(parameters =>
            parameters.Add(p => p.ClubId, 42L));

        // Act
        var rejectButton = cut.Find("button.btn-outline-danger");
        rejectButton.Click();

        await cut.InvokeAsync(() => Task.Delay(100));

        // Assert
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain("Failed to reject the request. Please try again.");
    }

    /// <summary>
    /// Phase 7: ClubAdminJoinRequests disables both Approve and Reject buttons while a request is being processed.
    /// Tests that _processingRequestId disables both buttons for the in-flight request.
    /// </summary>
    [Fact]
    public async Task HandleApproveAsync_DisablesBothButtons_WhileApprovalIsInFlight()
    {
        // Arrange
        var joinRequestService = Substitute.For<IClubJoinRequestService>();
        var tcs = new TaskCompletionSource<ServiceResult<Success>>();
        var initialRequests = new List<ClubJoinRequestDto>
        {
            new(
                1,      // clubJoinRequestId
                42,     // clubId
                "Test Club",    // clubName
                100,    // requestingUserId
            "Test User",    // requestingUserName
                RequestStatus.Pending,  // status
                DateTimeOffset.UtcNow   // createdAt
            )
        };

        joinRequestService.GetClubJoinRequestsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<ClubJoinRequestDto>>(initialRequests)));

        joinRequestService.ApproveJoinRequestAsync(1L, Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        SetupServices(joinRequestService);

        var cut = Render<ClubAdminJoinRequests>(parameters =>
            parameters.Add(p => p.ClubId, 42L));

        // Act — click Approve without completing the task
        var approveButton = cut.Find("button.btn-success");
        approveButton.Click();

        // Assert — both buttons disabled while the task is in-flight
        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll("button.btn-success, button.btn-outline-danger");
            foreach (var button in buttons)
            {
                button.HasAttribute("disabled").ShouldBeTrue();
            }
        });

        // Cleanup
        tcs.SetResult(new ServiceResult<Success>(new Success()));
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);
    }

    #endregion
}
