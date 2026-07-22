using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Account;
using Nova.Shared.Results;
using Nova.UI.Features.Account.Components;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Components;

/// <summary>
/// Tests for AssignClubAdminPanel component: member loading, selection UI, admin assignment,
/// navigation refresh, and error handling.
/// </summary>
public class AssignClubAdminPanelTests
{
    private sealed class PersistedStateAssignClubAdminPanel(
        IClubMemberService clubMemberService,
        NavigationManager navigationManager)
        : AssignClubAdminPanel(clubMemberService, navigationManager)
    {
        [Parameter]
        public bool StartInitialized { get; set; }

        [Parameter]
        public IReadOnlyList<ClubMemberDto>? PersistedMembers { get; set; }

        [Parameter]
        public bool PersistNullMembers { get; set; }

        [Parameter]
        public string? PersistedError { get; set; }

        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                Members = PersistNullMembers ? null! : PersistedMembers ?? [];
                Error = PersistedError;
            }

            return base.OnInitializedAsync();
        }
    }

    [Fact]
    public void OnInitialized_DoesNotFetchMembers_WhenPersistedMembersAreAvailable()
    {
        // Arrange
        var persistedMembers = new[]
        {
            new ClubMemberDto(7, "Persisted Member"),
        };
        var service = Substitute.For<IClubMemberService>();
        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<PersistedStateAssignClubAdminPanel>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedMembers, persistedMembers));

        // Assert
        service.DidNotReceive().GetClubMembersAsync(Arg.Any<CancellationToken>());
        cut.Markup.ShouldContain("Persisted Member");
    }

    [Fact]
    public void OnInitialized_DoesNotFetchMembers_WhenPersistedErrorExists()
    {
        // Arrange
        const string persistedError = "Persisted fetch error";
        var service = Substitute.For<IClubMemberService>();
        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<PersistedStateAssignClubAdminPanel>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedError, persistedError));

        // Assert
        service.DidNotReceive().GetClubMembersAsync(Arg.Any<CancellationToken>());
        cut.Markup.ShouldContain(persistedError);
        cut.Markup.ShouldContain("alert-danger");
    }

    [Fact]
    public void Render_ShowsEmptyState_WhenPersistedMembersStateIsNull()
    {
        // Arrange
        var service = Substitute.For<IClubMemberService>();
        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<PersistedStateAssignClubAdminPanel>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistNullMembers, true));

        // Assert
        cut.Markup.ShouldContain("No other club members are available to promote to admin");
        cut.Markup.ShouldNotContain("alert-danger");
    }

    [Fact]
    public async Task AssignAsync_RefreshesPage_OnSuccess_WithPersistedMembers()
    {
        // Arrange
        var persistedMembers = new[]
        {
            new ClubMemberDto(1, "Alice Johnson"),
        };
        var service = Substitute.For<IClubMemberService>();
        service
            .AssignClubAdminAsync(Arg.Any<AssignAdminInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<bool>)true));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<PersistedStateAssignClubAdminPanel>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedMembers, persistedMembers));

        cut.Find("input[type='radio'][value='1']").Change(true);
        cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin"))!.Click();

        await cut.InvokeAsync(() => { });

        // Assert
        await service.Received(1).AssignClubAdminAsync(
            Arg.Is<AssignAdminInput>(i => i != null && i.TargetUserId == 1L),
            Arg.Any<CancellationToken>());
        navigationManager.Received(1).Refresh(forceReload: true);
    }

    [Fact]
    public async Task AssignAsync_DisplaysError_OnFailure_WithPersistedMembers()
    {
        // Arrange
        var persistedMembers = new[]
        {
            new ClubMemberDto(1, "Alice Johnson"),
        };
        var submissionProblem = new ServiceProblem(
            ServiceProblemKind.Conflict,
            "Persisted-state submit failure");
        var service = Substitute.For<IClubMemberService>();
        service
            .AssignClubAdminAsync(Arg.Any<AssignAdminInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<bool>)submissionProblem));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<PersistedStateAssignClubAdminPanel>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedMembers, persistedMembers));

        cut.Find("input[type='radio'][value='1']").Change(true);
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin"));
        assignButton.ShouldNotBeNull();
        assignButton!.Click();

        await cut.InvokeAsync(() => { });

        // Assert
        cut.Markup.ShouldContain("Persisted-state submit failure");
        cut.Markup.ShouldContain("form-check");
        cut.Markup.ShouldContain("Alice Johnson");
        assignButton.HasAttribute("disabled").ShouldBeFalse();

        assignButton.Click();
        await cut.InvokeAsync(() => { });

        await service.Received(2).AssignClubAdminAsync(
            Arg.Is<AssignAdminInput>(i => i != null && i.TargetUserId == 1L),
            Arg.Any<CancellationToken>());
        navigationManager.DidNotReceive().Refresh(forceReload: Arg.Any<bool>());
    }

    [Fact]
    public async Task OnInitialized_LoadsMembers_OnSuccess()
    {
        // Arrange
        var members = new[]
        {
            new ClubMemberDto(1, "Alice Johnson"),
            new ClubMemberDto(2, "Bob Smith"),
        };
        var service = Substitute.For<IClubMemberService>();
        service
            .GetClubMembersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<IReadOnlyList<ClubMemberDto>>)members.ToList()));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<AssignClubAdminPanel>();

        // Wait for async initialization
        await cut.InvokeAsync(() => { });

        // Assert
        cut.Markup.ShouldContain("Alice Johnson");
        cut.Markup.ShouldContain("Bob Smith");
    }

    [Fact]
    public async Task OnInitialized_DisplaysEmptyState_WhenNoMembers()
    {
        // Arrange
        var emptyMembers = new List<ClubMemberDto>();
        var service = Substitute.For<IClubMemberService>();
        service
            .GetClubMembersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<IReadOnlyList<ClubMemberDto>>)emptyMembers));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<AssignClubAdminPanel>();

        await cut.InvokeAsync(() => { });

        // Assert
        cut.Markup.ShouldContain("No other club members are available to promote to admin");
    }

    [Fact]
    public async Task OnInitialized_DisplaysError_WhenGetMembersFails()
    {
        // Arrange
        var problem = new ServiceProblem(
            ServiceProblemKind.ServerError,
            "Failed to load members from database"
        );
        var service = Substitute.For<IClubMemberService>();
        service
            .GetClubMembersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<IReadOnlyList<ClubMemberDto>>)problem));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<AssignClubAdminPanel>();

        await cut.InvokeAsync(() => { });

        // Assert
        cut.Markup.ShouldContain("Failed to load members from database");
        cut.Markup.ShouldContain("alert-danger");
    }

    [Fact]
    public async Task AssignButton_IsDisabled_WhenNoMemberSelected()
    {
        // Arrange
        var members = new[]
        {
            new ClubMemberDto(1, "Alice Johnson"),
        };
        var service = Substitute.For<IClubMemberService>();
        service
            .GetClubMembersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<IReadOnlyList<ClubMemberDto>>)members.ToList()));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<AssignClubAdminPanel>();

        await cut.InvokeAsync(() => { });

        // Assert
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin"));
        assignButton.ShouldNotBeNull();
        assignButton!.HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task AssignButton_IsEnabled_AfterMemberSelection()
    {
        // Arrange
        var members = new[]
        {
            new ClubMemberDto(1, "Alice Johnson"),
        };
        var service = Substitute.For<IClubMemberService>();
        service
            .GetClubMembersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<IReadOnlyList<ClubMemberDto>>)members.ToList()));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<AssignClubAdminPanel>();

        await cut.InvokeAsync(() => { });

        var radio = cut.Find("input[type='radio'][value='1']");
        radio.Change(true);

        // Assert
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin"));
        assignButton.ShouldNotBeNull();
        assignButton!.HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public async Task AssignAsync_CallsService_WithSelectedUserId()
    {
        // Arrange
        var members = new[]
        {
            new ClubMemberDto(1, "Alice Johnson"),
        };
        var service = Substitute.For<IClubMemberService>();
        service
            .GetClubMembersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<IReadOnlyList<ClubMemberDto>>)members.ToList()));
        service
            .AssignClubAdminAsync(Arg.Any<AssignAdminInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<bool>)true));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<AssignClubAdminPanel>();

        await cut.InvokeAsync(() => { });

        var radio = cut.Find("input[type='radio'][value='1']");
        radio.Change(true);
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin"));
        assignButton!.Click();

        await cut.InvokeAsync(() => { });

        // Assert
        await service.Received(1).AssignClubAdminAsync(Arg.Is<AssignAdminInput>(i => i != null && i.TargetUserId == 1L), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignAsync_RefreshesPage_OnSuccess()
    {
        // Arrange
        var members = new[]
        {
            new ClubMemberDto(1, "Alice Johnson"),
        };
        var service = Substitute.For<IClubMemberService>();
        service
            .GetClubMembersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<IReadOnlyList<ClubMemberDto>>)members.ToList()));
        service
            .AssignClubAdminAsync(Arg.Any<AssignAdminInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<bool>)true));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<AssignClubAdminPanel>();

        await cut.InvokeAsync(() => { });

        var radio = cut.Find("input[type='radio'][value='1']");
        radio.Change(true);
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin"));
        assignButton!.Click();

        await cut.InvokeAsync(() => { });

        // Assert
        navigationManager.Received(1).Refresh(forceReload: true);
    }

    [Fact]
    public async Task AssignAsync_DisplaysError_OnFailure()
    {
        // Arrange
        var members = new[]
        {
            new ClubMemberDto(1, "Alice Johnson"),
        };
        var service = Substitute.For<IClubMemberService>();
        service
            .GetClubMembersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<IReadOnlyList<ClubMemberDto>>)members.ToList()));

        var problem = new ServiceProblem(
            ServiceProblemKind.Conflict,
            "This user is already a club admin"
        );
        service
            .AssignClubAdminAsync(Arg.Any<AssignAdminInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<bool>)problem));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<AssignClubAdminPanel>();

        await cut.InvokeAsync(() => { });

        var radio = cut.Find("input[type='radio'][value='1']");
        radio.Change(true);
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin"));
        assignButton.ShouldNotBeNull();
        assignButton!.Click();

        await cut.InvokeAsync(() => { });

        // Assert
        cut.Markup.ShouldContain("This user is already a club admin");
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain("form-check");
        cut.Markup.ShouldContain("Alice Johnson");
        assignButton.HasAttribute("disabled").ShouldBeFalse();

        assignButton.Click();
        await cut.InvokeAsync(() => { });

        await service.Received(2).AssignClubAdminAsync(
            Arg.Is<AssignAdminInput>(i => i != null && i.TargetUserId == 1L),
            Arg.Any<CancellationToken>());
        navigationManager.DidNotReceive().Refresh(forceReload: Arg.Any<bool>());
    }

    [Fact]
    public async Task OnInitialized_LoadingState_DisplaysLoadingMessage_WhileFetchingMembers()
    {
        // Arrange - simulate slow service that takes a moment to return
        var members = new[]
        {
            new ClubMemberDto(1, "Alice Johnson"),
        };
        var tcs = new TaskCompletionSource<ServiceResult<IReadOnlyList<ClubMemberDto>>>();
        var service = Substitute.For<IClubMemberService>();
        service
            .GetClubMembersAsync(Arg.Any<CancellationToken>())
            .Returns(tcs.Task);

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act - Initial render should show loading state
        var cut = testContext.Render<AssignClubAdminPanel>();

        await cut.InvokeAsync(() => { });

        // Assert
        cut.Markup.ShouldContain("Loading club members");
        cut.Markup.ShouldNotContain("alert-danger");
    }
}
