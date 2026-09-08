using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Features.Account;
using Nova.SharedKernel.Results;
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
#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
    private sealed class PersistedStateAssignClubAdminPanel(
#pragma warning restore CA1812
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
    public void OnInitializedDoesNotFetchMembersWhenPersistedMembersAreAvailable()
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
        _ = service.DidNotReceive().GetClubMembersAsync(Arg.Any<CancellationToken>());
        cut.Markup.ShouldContain("Persisted Member");
    }

    [Fact]
    public void OnInitializedDoesNotFetchMembersWhenPersistedErrorExists()
    {
        // Arrange
        const string PersistedError = "Persisted fetch error";
        var service = Substitute.For<IClubMemberService>();
        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<PersistedStateAssignClubAdminPanel>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedError, PersistedError));

        // Assert
        _ = service.DidNotReceive().GetClubMembersAsync(Arg.Any<CancellationToken>());
        cut.Markup.ShouldContain(PersistedError);
        cut.Markup.ShouldContain("alert-danger");
    }

    [Fact]
    public void RenderShowsEmptyStateWhenPersistedMembersStateIsNull()
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
    public async Task AssignAsyncRefreshesPageOnSuccessWithPersistedMembersAsync()
    {
        // Arrange
        var persistedMembers = new[]
        {
            new ClubMemberDto(1, "Alice Johnson"),
        };
        var service = Substitute.For<IClubMemberService>();
        service
            .PromoteMemberAsync(Arg.Any<ClubMemberMutationInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<OneOf.Types.Success>)new OneOf.Types.Success()));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<PersistedStateAssignClubAdminPanel>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedMembers, persistedMembers));

#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cut.Find("input[type='radio'][value='1']").Change(true);
#pragma warning restore CA1849, S6966
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin", StringComparison.Ordinal))!.Click();
#pragma warning restore CA1849, S6966

        await cut.InvokeAsync(() => { });

        // Assert
        await service.Received(1).PromoteMemberAsync(
            Arg.Is<ClubMemberMutationInput>(input => input.MemberUserId == 1),
            Arg.Any<CancellationToken>());
        navigationManager.Received(1).Refresh(forceReload: true);
    }

    [Fact]
    public async Task AssignAsyncDisplaysErrorOnFailureWithPersistedMembersAsync()
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
            .PromoteMemberAsync(Arg.Any<ClubMemberMutationInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<OneOf.Types.Success>)submissionProblem));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<PersistedStateAssignClubAdminPanel>(parameters => parameters
            .Add(p => p.StartInitialized, true)
            .Add(p => p.PersistedMembers, persistedMembers));

#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        cut.Find("input[type='radio'][value='1']").Change(true);
#pragma warning restore CA1849, S6966
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin", StringComparison.Ordinal));
        assignButton.ShouldNotBeNull();
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        assignButton.Click();
#pragma warning restore CA1849, S6966

        await cut.InvokeAsync(() => { });

        // Assert
        cut.Markup.ShouldContain("Persisted-state submit failure");
        cut.Markup.ShouldContain("form-check");
        cut.Markup.ShouldContain("Alice Johnson");
        assignButton.HasAttribute("disabled").ShouldBeFalse();

#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        assignButton.Click();
#pragma warning restore CA1849, S6966
        await cut.InvokeAsync(() => { });

        await service.Received(2).PromoteMemberAsync(
            Arg.Is<ClubMemberMutationInput>(input => input.MemberUserId == 1),
            Arg.Any<CancellationToken>());
        navigationManager.DidNotReceive().Refresh(forceReload: Arg.Any<bool>());
    }

    [Fact]
    public async Task OnInitializedLoadsMembersOnSuccessAsync()
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
    public async Task OnInitializedDisplaysEmptyStateWhenNoMembersAsync()
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
    public async Task OnInitializedDisplaysErrorWhenGetMembersFailsAsync()
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
    public async Task AssignButtonIsDisabledWhenNoMemberSelectedAsync()
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
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin", StringComparison.Ordinal));
        assignButton.ShouldNotBeNull();
        assignButton.HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task AssignButtonIsEnabledAfterMemberSelectionAsync()
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
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        radio.Change(true);
#pragma warning restore CA1849, S6966

        // Assert
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin", StringComparison.Ordinal));
        assignButton.ShouldNotBeNull();
        assignButton.HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public async Task AssignAsyncCallsServiceWithSelectedUserIdAsync()
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
            .PromoteMemberAsync(Arg.Any<ClubMemberMutationInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<OneOf.Types.Success>)new OneOf.Types.Success()));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<AssignClubAdminPanel>();

        await cut.InvokeAsync(() => { });

        var radio = cut.Find("input[type='radio'][value='1']");
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        radio.Change(true);
#pragma warning restore CA1849, S6966
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin", StringComparison.Ordinal));
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        assignButton!.Click();
#pragma warning restore CA1849, S6966

        await cut.InvokeAsync(() => { });

        // Assert
        await service.Received(1).PromoteMemberAsync(
            Arg.Is<ClubMemberMutationInput>(input => input.MemberUserId == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignAsyncRefreshesPageOnSuccessAsync()
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
            .PromoteMemberAsync(Arg.Any<ClubMemberMutationInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<OneOf.Types.Success>)new OneOf.Types.Success()));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<AssignClubAdminPanel>();

        await cut.InvokeAsync(() => { });

        var radio = cut.Find("input[type='radio'][value='1']");
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        radio.Change(true);
#pragma warning restore CA1849, S6966
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin", StringComparison.Ordinal));
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        assignButton!.Click();
#pragma warning restore CA1849, S6966

        await cut.InvokeAsync(() => { });

        // Assert
        navigationManager.Received(1).Refresh(forceReload: true);
    }

    [Fact]
    public async Task AssignAsyncDisplaysErrorOnFailureAsync()
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
            .PromoteMemberAsync(Arg.Any<ClubMemberMutationInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((ServiceResult<OneOf.Types.Success>)problem));

        var navigationManager = Substitute.For<NavigationManager>();

        using var testContext = new BunitContext();
        testContext.Services.AddScoped(_ => service);
        testContext.Services.AddScoped(_ => navigationManager);

        // Act
        var cut = testContext.Render<AssignClubAdminPanel>();

        await cut.InvokeAsync(() => { });

        var radio = cut.Find("input[type='radio'][value='1']");
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        radio.Change(true);
#pragma warning restore CA1849, S6966
        var assignButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Make this person a club admin", StringComparison.Ordinal));
        assignButton.ShouldNotBeNull();
#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        assignButton.Click();
#pragma warning restore CA1849, S6966

        await cut.InvokeAsync(() => { });

        // Assert
        cut.Markup.ShouldContain("This user is already a club admin");
        cut.Markup.ShouldContain("alert-danger");
        cut.Markup.ShouldContain("form-check");
        cut.Markup.ShouldContain("Alice Johnson");
        assignButton.HasAttribute("disabled").ShouldBeFalse();

#pragma warning disable CA1849, S6966 // Synchronous bUnit dispatch preserves the intermediate state being tested; the assertions control when async work has completed.
        assignButton.Click();
#pragma warning restore CA1849, S6966
        await cut.InvokeAsync(() => { });

        await service.Received(2).PromoteMemberAsync(
            Arg.Is<ClubMemberMutationInput>(input => input.MemberUserId == 1),
            Arg.Any<CancellationToken>());
        navigationManager.DidNotReceive().Refresh(forceReload: Arg.Any<bool>());
    }

    [Fact]
    public async Task OnInitializedLoadingStateDisplaysLoadingMessageWhileFetchingMembersAsync()
    {
        // Arrange - simulate slow service that takes a moment to return
        _ = new[]
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
