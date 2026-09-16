using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;
using PlayerForm = Nova.UI.Features.Players.Components.PlayerForm;
using PlayersPage = Nova.UI.Features.Players.Pages.Players;

namespace Nova.Unit.Tests.Players;

public sealed partial class PlayerComponentsTests
{
    /// <summary>Validation feedback cannot settle an earlier uncertain attempt or unlock a replacement payload.</summary>
    [Fact]
    public async Task PlayersRetainsPendingCreationAfterValidationFailureAsync()
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<CreatePlayerInput>();
            commands.Add(input);
            ServiceResult<PlayerCreationCompletion> result = commands.Count switch
            {
                1 => ServiceProblem.ServerError("Lost acknowledgement"),
                2 => ServiceProblem.Validation("FirstName", "Unexpected validation response"),
                _ => CreationCompletion(input)
            };
            return Task.FromResult(result);
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = Render<PlayersPage>();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));

        await FillAndSubmitAsync(cut);
        await cut.Find("button[type='submit']").ClickAsync(new());
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.Markup.ShouldContain("retry it unchanged");
        cut.FindComponent<PlayerForm>().Instance.Model.FirstName = "Replacement must not escape";
        await cut.Find("button[type='submit']").ClickAsync(new());

        commands.Count.ShouldBe(3);
        commands[1].ShouldBeSameAs(commands[0]);
        commands[2].ShouldBeSameAs(commands[0]);
        commands[2].FirstName.ShouldBe("Taylor");
        cut.Markup.ShouldContain("Enrolled in Original campaign.");
    }

    /// <summary>Late creation results cannot settle or unfreeze work owned by a newer club identity.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlayersIgnoresLateCreationResultWhileNewClubCreationIsPendingAsync(bool oldSucceeded)
    {
        var first = new TaskCompletionSource<ServiceResult<PlayerCreationCompletion>>();
        var second = new TaskCompletionSource<ServiceResult<PlayerCreationCompletion>>();
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            commands.Add(call.Arg<CreatePlayerInput>());
            return commands.Count switch
            {
                1 => first.Task,
                2 => second.Task,
                _ => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(CreationCompletion(commands[^1])))
            };
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<PlayersPage>();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        var oldSubmit = FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => commands.Count.ShouldBe(1));
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, clubId: 43)));
        var newSubmit = FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => commands.Count.ShouldBe(2));
        await cut.InvokeAsync(() => first.SetResult(oldSucceeded
            ? new ServiceResult<PlayerCreationCompletion>(CreationCompletion(commands[0]))
            : new ServiceResult<PlayerCreationCompletion>(ServiceProblem.ServerError("Old failure"))));
        await oldSubmit;
        cut.FindComponent<PlayerForm>().Instance.IsSubmitting.ShouldBeTrue();
        cut.Markup.ShouldNotContain("Old failure");
        cut.Markup.ShouldNotContain("Player created successfully.");
        await cut.InvokeAsync(() => second.SetResult(ServiceProblem.ServerError("New uncertainty")));
        await newSubmit;
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        await cut.Find("button[type='submit']").ClickAsync(new());
        commands.Count.ShouldBe(3);
        commands[2].ShouldBeSameAs(commands[1]);
        commands[2].ClubId.ShouldBe(43);
        commands[2].OperationId.ShouldNotBe(commands[0].OperationId);
    }

    /// <summary>Uncertain transport retains a frozen command, even across a role-only identity refresh.</summary>
    [Fact]
    public async Task PlayersRetriesExactPendingPayloadAcrossAdministratorDemotionAsync()
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<CreatePlayerInput>();
            commands.Add(input);
            return Task.FromResult(commands.Count == 1
                ? new ServiceResult<PlayerCreationCompletion>(ServiceProblem.ServerError("Lost response"))
                : new ServiceResult<PlayerCreationCompletion>(CreationCompletion(input)));
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<PlayersPage>();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue());
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(false)));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#player-first-name").ShouldBeEmpty());
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true)));
        await cut.Find("button.btn-primary").ClickAsync(new());
        cut.FindComponent<PlayerForm>().Instance.Model.FirstName = "Programmatic edit";
        await cut.Find("button[type='submit']").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Enrolled in Original campaign."));
        commands.Count.ShouldBe(2);
        commands[1].ShouldBeSameAs(commands[0]);
        commands[1].FirstName.ShouldBe("Taylor");
        await FillAndSubmitAsync(cut);
        commands.Count.ShouldBe(3);
        commands[2].OperationId.ShouldNotBe(commands[0].OperationId);
    }

    /// <summary>A receipt-backed duplicate permits correction with a new logical operation.</summary>
    [Fact]
    public async Task PlayersDuplicateRejectionAllowsCorrectionWithNewOperationAsync()
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<CreatePlayerInput>();
            commands.Add(input);
            return Task.FromResult(commands.Count == 1
                ? new ServiceResult<PlayerCreationCompletion>(PlayerCreationProblems.Duplicate(input.OperationId, 21, LifecycleStatus.Archived))
                : new ServiceResult<PlayerCreationCompletion>(CreationCompletion(input)));
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = Render<PlayersPage>();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("View existing archived player"));
        cut.Find("a[href='/players/21']").ShouldNotBeNull();
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Corrected" });
        await cut.Find("button[type='submit']").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Player created successfully."));
        commands.Count.ShouldBe(2);
        commands[1].OperationId.ShouldNotBe(commands[0].OperationId);
        commands[1].FirstName.ShouldBe("Corrected");
    }

    private static async Task FillAndSubmitAsync(IRenderedComponent<PlayersPage> cut)
    {
        await cut.Find("button.btn-primary").ClickAsync(new());
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Taylor" });
        await cut.Find("#player-last-name").ChangeAsync(new() { Value = "Lane" });
        await cut.Find("button[type='submit']").ClickAsync(new());
    }

    private static PlayerCreationCompletion CreationCompletion(CreatePlayerInput input) => new()
    {
        OperationId = input.OperationId,
        CompletedAt = DateTimeOffset.UtcNow,
        RecoveryExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
        Enrollment = new PlayerCreationEnrollment { CampaignId = 4, CampaignName = "Original campaign", PlayerCampaignAssignmentId = 8 },
        Player = new PlayerDto
        {
            PlayerId = 21,
            ClubId = input.ClubId,
            FirstName = input.FirstName,
            LastName = input.LastName,
            DateOfBirth = input.DateOfBirth,
            GraduationYear = input.GraduationYear,
            LifecycleStatus = LifecycleStatus.Active
        }
    };
}
