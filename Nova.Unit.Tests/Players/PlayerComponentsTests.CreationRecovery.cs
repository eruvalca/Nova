using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Players.Services;
using NSubstitute;
using Shouldly;
using PlayerCreationRecoveryState = Nova.UI.Features.Players.Components.PlayerCreationRecoveryState;
using PlayerFormState = Nova.UI.Features.Players.Components.PlayerFormState;
using PlayerIntakeBoard = Nova.UI.Features.Players.Components.PlayerIntakeBoard;
using PlayersPage = Nova.UI.Features.Players.Pages.Players;

namespace Nova.Unit.Tests.Players;

public sealed partial class PlayerComponentsTests
{
    [Theory]
    [InlineData("uncertain")]
    [InlineData("duplicate")]
    [InlineData("success")]
    public async Task ReopenedPendingCreationReceivesItsOwnCompletionAsync(string outcome)
    {
        var pending = new TaskCompletionSource<ServiceResult<PlayerCreationCompletion>>();
        CreatePlayerInput? command = null;
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            command = call.Arg<CreatePlayerInput>();
            return pending.Task;
        });
        RegisterServices(isClubAdmin: false, managementService: service);
        var cut = RenderPlayers();
        var save = FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => command.ShouldNotBeNull());
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a[href='/players']"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        cut.Find("#player-first-name").GetAttribute("value").ShouldBe("Taylor");
        await cut.InvokeAsync(() => pending.SetResult(outcome switch
        {
            "success" => new(CreationCompletion(command!)),
            "duplicate" => new(PlayerCreationProblems.Duplicate(command!.OperationId, 7, LifecycleStatus.Active)),
            _ => new(ServiceProblem.ServerError("Lost acknowledgement"))
        }));
        await save;
        if (string.Equals(outcome, "success", StringComparison.Ordinal))
        {
            // The board becomes the receipt in place; it does not navigate away.
            cut.Markup.ShouldContain("Enrolled in Original campaign.");
            cut.Find("#intake-add-another").ShouldNotBeNull();
            cut.Find("#intake-view-player").ShouldNotBeNull();
            cut.Find("#intake-return").ShouldNotBeNull();
            Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/players/new");
            Interop.Read(101, 42).ShouldBeNull();
        }
        else if (string.Equals(outcome, "duplicate", StringComparison.Ordinal))
        {
            cut.Markup.ShouldContain("View existing player");
            cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        }
        else
        {
            cut.Markup.ShouldContain("Lost acknowledgement");
            cut.Markup.ShouldContain("Replay the retained addition");
            cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        }
    }

    /// <summary>A creation that answers after the member left the form keeps its exact request recoverable.</summary>
    [Fact]
    public async Task PlayersKeepsACommittedCreationRecoverableWhenTheRouteChangesMidFlightAsync()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlayerCreationCompletion>>();
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            commands.Add(call.Arg<CreatePlayerInput>());
            return pending.Task;
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        var save = FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => commands.Count.ShouldBe(1));

        // The member leaves the form while the request is still in flight, and the server commits it.
        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/players"));
        await cut.InvokeAsync(() => pending.SetResult(new(CreationCompletion(commands[0]))));
        await save;

        // Nothing about the committed operation is published into the directory view, and its exact
        // command is still retained: that record is what makes the receipt recoverable rather than lost.
        await cut.WaitForAssertionAsync(() => Interop.Read(101, 42).ShouldNotBeNull());
        cut.FindAll("#intake-view-player").Count.ShouldBe(0);
        cut.Markup.ShouldNotContain("Player created.");

        // Returning to the form recovers the same operation and settles it from the server's receipt.
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Replay the retained addition"));
        await cut.Find("#intake-submit").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Enrolled in Original campaign."));
        commands.Count.ShouldBe(2);
        commands[1].ShouldBeSameAs(commands[0]);
        Interop.Read(101, 42).ShouldBeNull();
    }

    /// <summary>The retained record cannot be resolved while its own submission is in flight.</summary>
    [Fact]
    public async Task PlayersKeepsTheRetainedAdditionUnresolvableWhileTheSubmissionIsInFlightAsync()
    {
        var dispatch = new TaskCompletionSource<ServiceResult<PlayerCreationCompletion>>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => dispatch.Task);
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));

        var save = FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));

        // While the answer is outstanding the retained request is the operation's only recovery copy, so the
        // actions that would resolve it are not offered: setting it aside now would leave a committed response
        // with no receipt to show and an unknown one with nothing to replay after a reload.
        cut.Find("#intake-unresolved button").HasAttribute("disabled").ShouldBeTrue();
        var retained = Interop.Read(101, 42);
        retained.ShouldNotBeNull();

        // The answer arrives with the copy intact, so the receipt is shown and the record released.
        await cut.InvokeAsync(() => dispatch.SetResult(new(CreationCompletion(retained.Payload))));
        await save;

        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1));
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0);
        Interop.Read(101, 42).ShouldBeNull();
    }

    /// <summary>Validation feedback cannot settle an earlier uncertain attempt or unlock a replacement payload.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("FirstName")]
    [InlineData("OperationId")]
    public async Task PlayersRetainsPendingCreationAfterValidationFailureAsync(string field)
        => await AssertPendingCreationRetainedAsync(_ => ServiceProblem.Validation(field, "Unexpected validation response"));

    [Fact]
    public async Task PlayersRetainsPendingCreationAfterContradictoryDuplicateAsync()
        => await AssertPendingCreationRetainedAsync(input => PlayerCreationProblems.Duplicate(input.OperationId, 7, LifecycleStatus.Active) with
        {
            Errors = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["FirstName"] = ["Invalid value"] }
        });

    private async Task AssertPendingCreationRetainedAsync(Func<CreatePlayerInput, ServiceProblem> retryProblem)
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
                2 => retryProblem(input),
                _ => CreationCompletion(input)
            };
            return Task.FromResult(result);
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));

        await FillAndSubmitAsync(cut);
        await cut.Find("#intake-submit").ClickAsync(new());
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.Markup.ShouldContain("Replay the retained addition");
        cut.FindComponent<PlayerIntakeBoard>().Instance.Model.FirstName = "Replacement must not escape";
        await cut.Find("#intake-submit").ClickAsync(new());

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
        var cut = RenderPlayers();
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
        cut.FindComponent<PlayerIntakeBoard>().Instance.IsSubmitting.ShouldBeTrue();
        cut.Markup.ShouldNotContain("Old failure");
        cut.FindAll("#intake-receipt-heading").Count.ShouldBe(0);
        await cut.InvokeAsync(() => second.SetResult(ServiceProblem.ServerError("New uncertainty")));
        await newSubmit;
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        await cut.Find("#intake-submit").ClickAsync(new());
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
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue());
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(false)));
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue());
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true)));
        cut.FindComponent<PlayerIntakeBoard>().Instance.Model.FirstName = "Programmatic edit";
        await cut.Find("#intake-submit").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Enrolled in Original campaign."));
        commands.Count.ShouldBe(2);
        commands[1].ShouldBeSameAs(commands[0]);
        commands[1].FirstName.ShouldBe("Taylor");

        // The receipt offers another addition; only player-specific input resets.
        await cut.Find("#intake-add-another").ClickAsync(new());
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Taylor" });
        await cut.Find("#player-last-name").ChangeAsync(new() { Value = "Lane" });
        await cut.Find("#intake-submit").ClickAsync(new());
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
        const string RosterUrl = "/players?view=archived&search=Avery&graduationYear=2032&tag=11&returnToDraft=10&returnUrl=%2Fcampaigns%2F10%3Ftab%3Dclose";
        Services.GetRequiredService<NavigationManager>().NavigateTo(RosterUrl);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("View existing archived player"));
        cut.Find("a[href^='/players/21?']").GetAttribute("href")
            .ShouldBe($"/players/21?returnUrl={Uri.EscapeDataString(RosterUrl)}");
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Corrected" });
        await cut.Find("#intake-submit").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Player added"));
        commands.Count.ShouldBe(2);
        commands[1].OperationId.ShouldNotBe(commands[0].OperationId);
        commands[1].FirstName.ShouldBe("Corrected");
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "#intake-return"));
        await cut.WaitForAssertionAsync(() => cut.FindAll(".intake-receipt").Count.ShouldBe(0));
        cut.Markup.ShouldNotContain("View existing archived player");

        // The settled operation is not retained, so re-entering the board is unlocked.
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
    }

    /// <summary>A new operation does not inherit the previous refusal's duplicate panel.</summary>
    [Fact]
    public async Task PlayersStartsANewOperationWithoutThePreviousDuplicatePanelAsync()
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<CreatePlayerInput>();
            commands.Add(input);
            return Task.FromResult(commands.Count == 1
                ? new ServiceResult<PlayerCreationCompletion>(
                    PlayerCreationProblems.Duplicate(input.OperationId, 21, LifecycleStatus.Active))
                : new ServiceResult<PlayerCreationCompletion>(ServiceProblem.ServerError("Lost acknowledgement")));
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-duplicate").Count.ShouldBe(1));

        // The corrected form starts a new operation, and that operation's outcome is its own: the earlier
        // refusal's panel described input this attempt does not send, and while it stood it also withheld the
        // replay this operation offers.
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Corrected" });
        await cut.Find("#intake-submit").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));
        cut.FindAll("#intake-duplicate").Count.ShouldBe(0);
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-submit").TextContent
            .ShouldContain("Replay the retained addition"));
    }

    /// <summary>Closing a rejected form clears its duplicate feedback without retaining a settled operation.</summary>
    [Fact]
    public async Task PlayersClearsDuplicateWhenCancelledFormReopensAsync()
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<CreatePlayerInput>();
            commands.Add(input);
            return Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                PlayerCreationProblems.Duplicate(input.OperationId, 21, LifecycleStatus.Active)));
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        cut.Markup.ShouldContain("View existing player");

        await cut.Find("#intake-cancel").ClickAsync(new());

        // Cancel with typed input asks first, and the confirmed departure is the deliberate close this case is
        // about: the refusal's feedback goes with the input it described.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-departure").Count.ShouldBe(1));
        await cut.Find("#intake-departure button.btn-warning").ClickAsync(new());
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));

        cut.Markup.ShouldNotContain("View existing player");
        cut.FindComponent<PlayerIntakeBoard>().Instance.Duplicate.ShouldBeNull();
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse());
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Another" });
        await cut.Find("#player-last-name").ChangeAsync(new() { Value = "Lane" });
        await cut.Find("#intake-submit").ClickAsync(new());
        commands.Count.ShouldBe(2);
        commands[1].OperationId.ShouldNotBe(commands[0].OperationId);
        commands[1].FirstName.ShouldBe("Another");
    }

    /// <summary>Neither expiry nor denied recovery proves rollback; hiding the form cannot replace unresolved work.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task PlayersRetainsUnresolvedCreationAfterDenialAndCancelAsync(bool expired, bool decodedExtensions)
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            commands.Add(call.Arg<CreatePlayerInput>());
            var denial = expired ? PlayerCreationProblems.Expired() : ServiceProblem.Forbidden("Membership removed");
            if (decodedExtensions)
            {
                denial = denial with
                {
                    Extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [PlayerCreationProblems.ReasonExtension] = JsonSerializer.SerializeToElement("expired")
                    }
                };
            }
            var problem = commands.Count == 1 ? ServiceProblem.ServerError("Lost response") : denial;
            return Task.FromResult(new ServiceResult<PlayerCreationCompletion>(problem));
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.Find("#intake-submit").ClickAsync(new());
        cut.Markup.ShouldContain(expired ? "has expired" : "Membership removed");
        if (expired)
        {
            cut.Markup.ShouldContain("Review the Players directory");
            cut.Markup.ShouldNotContain("Replay the retained addition");
        }
        else { cut.Markup.ShouldContain("Replay the retained addition"); }
        cut.Markup.ShouldContain("retained the exact addition");

        await cut.Find("#intake-cancel").ClickAsync(new());
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.FindComponent<PlayerIntakeBoard>().Instance.Model.FirstName = "Replacement must not escape";

        // The stored command is still inside the client's own 24-hour window, so it stays replayable
        // and the server remains the authority on its outcome; nothing is inferred from the refusal.
        await cut.Find("#intake-submit").ClickAsync(new());
        commands.Count.ShouldBe(3);
        commands[1].ShouldBeSameAs(commands[0]);
        commands[2].ShouldBeSameAs(commands[0]);
        commands[2].FirstName.ShouldBe("Taylor");

        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
    }

    /// <summary>A settlement whose record another tab already removed reports the receipt, not a refusal.</summary>
    [Fact]
    public async Task PlayersShowsTheReceiptWhenTheRetainedRecordWasAlreadyGoneAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var input = call.Arg<CreatePlayerInput>();
                // The record leaves storage while this tab is dispatching: another tab settled the same
                // operation, or the member set the addition aside.
                await Interop.ClearAsync(101, 42, input.OperationId, CancellationToken.None);
                return new ServiceResult<PlayerCreationCompletion>(CreationCompletion(input));
            });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);

        // The receipt proves the operation and there is nothing left to release, so the board reports the
        // receipt alone: claiming the browser held bytes would strand the member on a retry that cannot work.
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Enrolled in Original campaign."));
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0);
        Interop.Read(101, 42).ShouldBeNull();
    }

    /// <summary>A refusal whose record another tab already removed is not reported as held bytes.</summary>
    [Fact]
    public async Task PlayersReportsARefusalWhoseRecordWasAlreadyGoneAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var input = call.Arg<CreatePlayerInput>();
                await Interop.ClearAsync(101, 42, input.OperationId, CancellationToken.None);
                return new ServiceResult<PlayerCreationCompletion>(
                    PlayerCreationProblems.Duplicate(input.OperationId, 21, LifecycleStatus.Active));
            });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);

        // The receipt-backed refusal settles the operation and nothing is left to release, so the refusal is
        // reported as itself — the board is not blocked on a release that has already happened elsewhere.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-duplicate").Count.ShouldBe(1));
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0);
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse();
    }

    /// <summary>A receipt-backed refusal settles the operation, so its retained command is never resent.</summary>
    [Fact]
    public async Task PlayersWithholdsReplayWhenTheRefusedRecordIsUnreleasedAsync()
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<CreatePlayerInput>();
            commands.Add(input);
            // The first attempt's outcome is unknown, which is what puts the replay on offer; the
            // replay is then refused against the existing player.
            return Task.FromResult(commands.Count == 1
                ? new ServiceResult<PlayerCreationCompletion>(ServiceProblem.ServerError("Lost response"))
                : new ServiceResult<PlayerCreationCompletion>(
                    PlayerCreationProblems.Duplicate(input.OperationId, 21, LifecycleStatus.Active)));
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        // The refusal is receipt-backed but its record cannot be released, so it stays retained.
        Interop.FailClears = true;
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Replay the retained addition"));

        await cut.Find("#intake-submit").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-duplicate").Count.ShouldBe(1));

        // The refusal proves this operation did not create, so the retained command is not offered back
        // as a replay: sending it again could only be refused again. The set-aside and the directory
        // remain the ways to resolve the record the browser still holds.
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#intake-submit").TextContent.ShouldBe("Create player");
        cut.Find("#intake-unresolved").TextContent.ShouldNotContain("Replay the retained addition");
        cut.Find("#intake-unresolved").TextContent.ShouldNotContain("result is unknown");
        commands.Count.ShouldBe(2);
        commands[1].ShouldBeSameAs(commands[0]);
    }

    /// <summary>A failed storage read keeps the in-memory retained command and never claims nothing was sent.</summary>
    [Fact]
    public async Task PlayersKeepsInMemoryRetainedCommandWhenStorageReadFailsAsync()
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            commands.Add(call.Arg<CreatePlayerInput>());
            return Task.FromResult(new ServiceResult<PlayerCreationCompletion>(ServiceProblem.ServerError("Lost response")));
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Replay the retained addition"));

        // Re-entering the board re-reads storage for the same owner. That read now fails, which says
        // nothing about the command this page already dispatched: it stays the only evidence of it.
        Interop.FailReads = true;
        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/players"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));

        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));
        cut.Find("#player-first-name").GetAttribute("value").ShouldBe("Taylor");
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1);
        cut.Find("#intake-storage-unavailable").TextContent.ShouldNotContain("Nothing has been sent from this board.");
        commands.Count.ShouldBe(1);
    }

    /// <summary>A recovery read from an earlier visit cannot replace input a newer read enabled.</summary>
    [Fact]
    public async Task PlayersRejectsARecoveryReadThatLandsAfterTheBoardWasReEnteredAsync()
    {
        var retained = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Retained",
            LastName = "Snapshot",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        };
        var heldRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Interop.ReadGate = heldRead;
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));

        // Entering the form starts a read the boundary holds open.
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));

        // The member leaves and returns while that read is still open. The new visit's read answers, so the
        // board offers its fields and the member types.
        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/players"));
        Interop.ReadGate = null;
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse());
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Taylor" });

        // The earlier read then answers with a retained command that appeared in storage since — the snapshot
        // it carries belongs to the visit the member left, so it must not be published over their input.
        Interop.Seed(new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(retained.OperationId, out var createdAt)
                ? createdAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow.AddHours(24),
            Payload = retained
        });
        await cut.InvokeAsync(() => heldRead.SetResult());

        // Let the answer reach the page before judging what it did with it: the boundary's answer, then the two
        // awaits it has to cross — the board's read, and the page's own continuation.
        await cut.WaitForAssertionAsync(() => Interop.ReadCount.ShouldBe(2));
        await cut.InvokeAsync(() => Task.CompletedTask);
        await cut.InvokeAsync(() => Task.CompletedTask);

        cut.Find("#player-first-name").GetAttribute("value").ShouldBe("Taylor");
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse();
    }

    /// <summary>A removal that answers after the member left cannot rewrite the form they returned to.</summary>
    [Fact]
    public async Task PlayersKeepsInputTypedAfterASetAsideAnsweredOnAnotherVisitAsync()
    {
        var retained = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Retained",
            LastName = "Snapshot",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        };
        Interop.Seed(new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(retained.OperationId, out var createdAt)
                ? createdAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow.AddHours(24),
            Payload = retained
        });
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));

        // The browser removes the record but its answer stays in flight.
        Interop.ClearResumeGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await cut.Find("#intake-unresolved button").ClickAsync(new());
        await cut.Find("#set-aside-acknowledge").ChangeAsync(new ChangeEventArgs { Value = true });
        var setAside = cut.Find("#intake-set-aside button.btn-warning").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => Interop.Read(101, 42).ShouldBeNull());

        // The member leaves and returns to a form with nothing retained, and types into it.
        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/players"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse());
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Taylor" });

        // The answer then arrives. The set-aside stands — the bytes are gone — but the visit that typed is not
        // the one that asked for it, so nothing it entered is rewritten and no report lands on it.
        Interop.ClearResumeGate.SetResult();
        await setAside;

        cut.Find("#player-first-name").GetAttribute("value").ShouldBe("Taylor");
        cut.Markup.ShouldNotContain("Retained addition set aside");
    }

    /// <summary>Entry withholds input until the retained command has actually been checked.</summary>
    [Fact]
    public async Task PlayersWithholdsTheBoardUntilTheRetainedCommandIsCheckedAsync()
    {
        Interop.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));

        // Until the read settles, the form must not offer input a landed recovery would replace.
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue());
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeTrue();
        Interop.ReadGate.SetResult();
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());

        // Re-entry re-reads the retained command, so the board withholds input again until it lands.
        Interop.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/players"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue());
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeTrue();
        Interop.ReadGate.SetResult();
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse();
    }

    /// <summary>Add another re-reads the retained command, so its fresh fields are withheld until it settles.</summary>
    [Fact]
    public async Task PlayersWithholdsTheBoardWhileAddAnotherReReadsTheRetainedCommandAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                CreationCompletion(call.Arg<CreatePlayerInput>()))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-add-another").Count.ShouldBe(1));

        // Add another re-reads storage, and the key is owner-scoped rather than tab-scoped, so a
        // command retained by another tab is exactly what this read can land.
        Interop.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var addAnother = cut.Find("#intake-add-another").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue());
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll("#intake-checking-note").Count.ShouldBe(1);

        // bUnit's static renderer still dispatches this change, where a browser refuses input inside
        // a disabled field set: the field is withheld, so no member could have typed here at all.
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Typed" });
        Interop.ReadGate.SetResult();
        await addAnother;
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());

        // A read that finds nothing retained leaves the typed value exactly as it was.
        cut.Find("#player-first-name").GetAttribute("value").ShouldBe("Typed");
    }

    /// <summary>Retry storage re-reads the retained command, so its fields are withheld until it settles.</summary>
    [Fact]
    public async Task PlayersWithholdsTheBoardWhileRetryStorageReReadsTheRetainedCommandAsync()
    {
        RegisterServices(isClubAdmin: true);
        Interop.FailWrites = true;
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));

        // Retry storage re-reads, and a command retained by another tab can land in that read, so the
        // fields those values would replace are withheld for its duration.
        Interop.FailWrites = false;
        Interop.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var retry = cut.Find("#intake-storage-unavailable button").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue());
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll("#intake-checking-note").Count.ShouldBe(1);

        // bUnit's static renderer still dispatches this change, where a browser refuses input inside
        // a disabled field set: the field is withheld, so no member could have typed here at all.
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Typed" });
        Interop.ReadGate.SetResult();
        await retry;
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());

        // The succeeded retry restores the board rather than stranding it, and keeps the typed value.
        cut.Find("#player-first-name").GetAttribute("value").ShouldBe("Typed");
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0);
    }

    /// <summary>
    /// A failed retained-command read keeps the board withheld until a read actually answers. This
    /// supersedes round 2's reopen, which could not protect values typed before a later retry landed a
    /// retained command over them.
    /// </summary>
    [Fact]
    public async Task PlayersKeepsTheBoardWithheldWhenTheRetainedCommandReadFailsAsync()
    {
        RegisterServices(isClubAdmin: true);
        Interop.FailReads = true;
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));

        // Storage refused the read, so the owner's retained state is unknown. The board may speak only
        // for itself about dispatch — nothing left this board — but it must not open its fields: values
        // typed now would be replaced without warning by whatever a later retry lands.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));
        var unread = cut.Find("#intake-storage-unavailable").TextContent;
        unread.ShouldContain("Nothing has been sent from this board.");
        // Storage being unread is not a verdict on the addition: the copy names what a retained request
        // does and does not mean, and the retry, rather than letting the member read it as the commit.
        unread.ShouldContain("only records that a send was attempted, never what the server decided.");
        unread.ShouldContain("Retry storage to check what it holds before adding.");
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#intake-checking-note").TextContent.ShouldContain("could not be checked");
        cut.Find("fieldset").GetAttribute("aria-describedby").ShouldBe("intake-checking-note");

        // The retry resolves it, and it is the only control that can: a read that answers opens the
        // board, so the withholding is a state the member can leave rather than a dead end.
        Interop.FailReads = false;
        await cut.Find("#intake-storage-unavailable button").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse();
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0);
        cut.FindAll("#intake-checking-note").Count.ShouldBe(0);
    }

    /// <summary>A retry whose read fails again keeps the board withheld instead of reopening it.</summary>
    [Fact]
    public async Task PlayersKeepsTheBoardWithheldWhenTheRetryReadFailsAgainAsync()
    {
        RegisterServices(isClubAdmin: true);
        Interop.FailReads = true;
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue());

        await cut.Find("#intake-storage-unavailable button").ClickAsync(new());

        // The second failure proves just as little as the first, so the fields stay closed and the check
        // stays unsettled rather than reopening the form over retained state nobody has read.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#intake-checking-note").TextContent.ShouldContain("could not be checked");
    }

    /// <summary>The board names a check in progress instead of a campaign fact while the read is open.</summary>
    [Fact]
    public async Task PlayersNamesTheEnrollmentCheckWhileTheIntakeConsequenceReadIsOpenAsync()
    {
        var context = new TaskCompletionSource<ServiceResult<PlayerIntakeContext>>();
        var intakeContext = Substitute.For<IPlayerIntakeContextService>();
        intakeContext.GetPlayerIntakeContextAsync(Arg.Any<GetPlayerIntakeContextInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => context.Task);
        RegisterServices(isClubAdmin: false, intakeContextService: intakeContext);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));

        await cut.WaitForAssertionAsync(() =>
            cut.Find("p.intake-consequence").TextContent.ShouldContain("Checking the enrollment consequence"));
        cut.Markup.ShouldNotContain("No campaign is Active");

        // The retained-command check has settled — the fields are open — so the one thing still withholding the
        // commit is the consequence the board is naming as unread.
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeTrue();

        await cut.InvokeAsync(() => context.SetResult(new ServiceResult<PlayerIntakeContext>(IntakeContext)));
        await cut.WaitForAssertionAsync(() => cut.Find("p.intake-consequence").TextContent.ShouldContain("Summer Tryouts"));
        cut.Markup.ShouldNotContain("Checking the enrollment consequence");
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse());
    }

    /// <summary>The attaching client adopts the enrollment consequence the prerendered page already showed.</summary>
    [Fact]
    public async Task PlayersAdoptsThePrerenderedIntakeConsequenceInsteadOfReadingItAgainAsync()
    {
        var intakeContext = Substitute.For<IPlayerIntakeContextService>();
        intakeContext.GetPlayerIntakeContextAsync(Arg.Any<GetPlayerIntakeContextInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ServiceResult<PlayerIntakeContext>(new PlayerIntakeContext
            {
                CampaignId = 9,
                CampaignName = "Stale Campaign"
            })));
        RegisterServices(isClubAdmin: true, intakeContextService: intakeContext);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/players/new");

        var cut = Render<SnapshotPlayers>(parameters => parameters
            .Add(component => component.RestoredScope, "101:42:True")
            .Add(component => component.RestoredIntakeContext, new PlayerIntakeContext
            {
                CampaignId = 5,
                CampaignName = "Summer Tryouts"
            }));

        // The prerender's answer is already on screen, so attaching reads nothing of the club while still
        // settling the gate the prerendered commit control was waiting on.
        await cut.WaitForAssertionAsync(() =>
            cut.Find("p.intake-consequence").TextContent.ShouldContain("Summer Tryouts"));
        cut.Find("p.intake-consequence").TextContent.ShouldNotContain("Stale Campaign");
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse());
        await intakeContext.DidNotReceive()
            .GetPlayerIntakeContextAsync(Arg.Any<GetPlayerIntakeContextInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A consequence the prerender recorded for another owner is never adopted.</summary>
    [Fact]
    public async Task PlayersReadsTheIntakeConsequenceThePrerenderRecordedForAnotherOwnerAsync()
    {
        var intakeContext = Substitute.For<IPlayerIntakeContextService>();
        intakeContext.GetPlayerIntakeContextAsync(Arg.Any<GetPlayerIntakeContextInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ServiceResult<PlayerIntakeContext>(IntakeContext)));
        RegisterServices(isClubAdmin: true, intakeContextService: intakeContext);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/players/new");

        var cut = Render<SnapshotPlayers>(parameters => parameters
            .Add(component => component.RestoredScope, "999:42:True")
            .Add(component => component.RestoredIntakeContext, new PlayerIntakeContext
            {
                CampaignId = 9,
                CampaignName = "Stale Campaign"
            }));

        await cut.WaitForAssertionAsync(() =>
            cut.Find("p.intake-consequence").TextContent.ShouldContain("Summer Tryouts"));
        cut.Find("p.intake-consequence").TextContent.ShouldNotContain("Stale Campaign");
        await intakeContext.Received(1)
            .GetPlayerIntakeContextAsync(Arg.Any<GetPlayerIntakeContextInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A prerendered consequence read that failed is adopted as that failure, not read again.</summary>
    [Fact]
    public async Task PlayersAdoptsThePrerenderedIntakeReadFailureInsteadOfReadingItAgainAsync()
    {
        var intakeContext = Substitute.For<IPlayerIntakeContextService>();
        intakeContext.GetPlayerIntakeContextAsync(Arg.Any<GetPlayerIntakeContextInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ServiceResult<PlayerIntakeContext>(IntakeContext)));
        RegisterServices(isClubAdmin: true, intakeContextService: intakeContext);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/players/new");

        var cut = Render<SnapshotPlayers>(parameters => parameters
            .Add(component => component.RestoredScope, "101:42:True")
            .Add(component => component.RestoredIntakeContextUnavailable, true));

        await cut.WaitForAssertionAsync(() => cut.Find("p.intake-consequence").TextContent
            .ShouldContain("enrollment consequence could not be read"));
        await intakeContext.DidNotReceive()
            .GetPlayerIntakeContextAsync(Arg.Any<GetPlayerIntakeContextInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A form re-entered in the same session asks the club again instead of adopting the first answer.</summary>
    [Fact]
    public async Task PlayersReadsTheIntakeConsequenceAgainWhenTheFormIsReenteredAsync()
    {
        var intakeContext = Substitute.For<IPlayerIntakeContextService>();
        intakeContext.GetPlayerIntakeContextAsync(Arg.Any<GetPlayerIntakeContextInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ServiceResult<PlayerIntakeContext>(IntakeContext)));
        RegisterServices(isClubAdmin: true, intakeContextService: intakeContext);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/players/new");
        var cut = RenderPlayers();

        await cut.WaitForAssertionAsync(() => cut.Find("p.intake-consequence").TextContent.ShouldContain("Summer Tryouts"));
        await intakeContext.Received(1)
            .GetPlayerIntakeContextAsync(Arg.Any<GetPlayerIntakeContextInput>(), Arg.Any<CancellationToken>());

        // The answer the prerendered page showed is published, so the client attaching to it has the same
        // consequence to adopt rather than a question to ask again.
        cut.Instance.PersistedIntakeContext.ShouldNotBeNull().CampaignName.ShouldBe("Summer Tryouts");
        cut.Instance.PersistedIntakeContextUnavailable.ShouldBeFalse();

        await cut.InvokeAsync(() => navigation.NavigateTo("/players"));
        await cut.WaitForAssertionAsync(() => cut.FindAll("p.intake-consequence").Count.ShouldBe(0));
        await cut.InvokeAsync(() => navigation.NavigateTo("/players/new"));

        // Only the prerender and attach pair is answered by the snapshot: a later entry is a new question.
        await cut.WaitForAssertionAsync(() => cut.Find("p.intake-consequence").TextContent.ShouldContain("Summer Tryouts"));
        await intakeContext.Received(2)
            .GetPlayerIntakeContextAsync(Arg.Any<GetPlayerIntakeContextInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The set-aside acknowledgement lives outside the EditForm, so only the module sees that input.</summary>
    [Fact]
    public async Task PlayersPromptsOnDepartureAfterTheSetAsideAcknowledgementAloneAsync()
    {
        var retained = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Taylor",
            LastName = "Lane",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        };
        Interop.Seed(new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(retained.OperationId, out var createdAt)
                ? createdAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow.AddHours(24),
            Payload = retained
        });
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));
        await cut.WaitForAssertionAsync(() => Interop.GuardAttached.ShouldBeTrue());

        await cut.Find("#intake-unresolved button").ClickAsync(new());
        await cut.Find("#set-aside-acknowledge").ChangeAsync(new ChangeEventArgs { Value = true });
        cut.Find("#intake-set-aside button.btn-warning").HasAttribute("disabled").ShouldBeFalse();
        // The acknowledgement never reaches the EditContext, so the board's own dirty flag stays clear.
        Interop.Dirty.ShouldBeFalse();

        // The module decides a prompt is due and calls back with its own lease, exactly as the
        // browser module does; the attempt must open the panel rather than swallow the click.
        await cut.InvokeAsync(() => cut.FindComponent<PlayerIntakeBoard>().Instance
            .OnBoardDepartureAttemptAsync(Interop.GuardLease!, "/players"));

        cut.FindAll("#intake-departure").Count.ShouldBe(1);
    }

    /// <summary>A board holding nothing that could be lost performs the departure its module cancelled.</summary>
    [Fact]
    public async Task PlayersPerformsTheDepartureWhenTheAttemptCannotLoseAnythingAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                CreationCompletion(call.Arg<CreatePlayerInput>()))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1));
        await cut.WaitForAssertionAsync(() => Interop.GuardAttached.ShouldBeTrue());

        // The module cancels the click and asks the board. The board has held a receipt since the
        // commit, so nothing can be lost: the cancelled click must depart rather than go inert.
        await cut.InvokeAsync(() => cut.FindComponent<PlayerIntakeBoard>().Instance
            .OnBoardDepartureAttemptAsync(Interop.GuardLease!, "/players?view=archived"));

        cut.FindAll("#intake-departure").Count.ShouldBe(0);
        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/players?view=archived");
    }

    /// <summary>A frozen board holds the exact addition that was sent, so leaving it loses nothing.</summary>
    [Fact]
    public async Task PlayersPerformsTheDepartureWhenTheFrozenRetainedAdditionCannotBeLostAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                ServiceProblem.ServerError("Lost acknowledgement"))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => Interop.GuardAttached.ShouldBeTrue());

        // The member types, so the guard speaks for input that is still unsaved.
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Taylor" });
        await cut.Find("#player-last-name").ChangeAsync(new() { Value = "Lane" });
        await cut.WaitForAssertionAsync(() => Interop.Dirty.ShouldBeTrue());

        // The acknowledgement never arrives, which freezes the board on the exact command it retained.
        await cut.Find("#intake-submit").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();

        // The frozen fields are the retained addition, not unsaved work, so the guard must stop
        // warning about them: a prompt here would claim input could be discarded that is never
        // discarded, and that the member cannot even edit.
        await cut.WaitForAssertionAsync(() => Interop.Dirty.ShouldBeFalse());

        await cut.InvokeAsync(() => cut.FindComponent<PlayerIntakeBoard>().Instance
            .OnBoardDepartureAttemptAsync(Interop.GuardLease!, "/players"));

        cut.FindAll("#intake-departure").Count.ShouldBe(0);
        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/players");
    }

    /// <summary>An operation whose own window has closed cannot be replayed and is never discarded.</summary>
    [Fact]
    public async Task PlayersTreatsAnExpiredRetainedOperationAsUnrecoverableAsync()
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            commands.Add(call.Arg<CreatePlayerInput>());
            return Task.FromResult(new ServiceResult<PlayerCreationCompletion>(CreationCompletion(call.Arg<CreatePlayerInput>())));
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var expired = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(DateTimeOffset.UtcNow.AddHours(-25)),
            ClubId = 42,
            FirstName = "Taylor",
            LastName = "Lane",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        };
        Interop.Seed(new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(expired.OperationId, out var createdAt)
                ? createdAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow,
            Payload = expired
        });

        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("24-hour window has closed"));
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeTrue();
        cut.Markup.ShouldContain("retained the exact addition");
        commands.ShouldBeEmpty();
    }

    /// <summary>A guard whose first attachment fails is retried once the boundary answers again.</summary>
    [Fact]
    public async Task PlayersRetriesTheDepartureGuardAfterATransientAttachFailureAsync()
    {
        RegisterServices(isClubAdmin: true);
        Interop.FailGuardAttachAttempts = 1;
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));

        // The mount's own retention read proves the boundary works again, and that is what re-arms
        // the attachment. Without the retry, typed input would stay unprotected for the whole mount
        // because the very first attempt failed.
        await cut.WaitForAssertionAsync(() => Interop.GuardAttached.ShouldBeTrue());
        Interop.GuardAttachCount.ShouldBe(2);
    }

    /// <summary>A mounting disposed while the guard attach is in flight still releases the lease it started.</summary>
    [Fact]
    public async Task PlayersReleasesTheGuardLeaseWhenDisposedDuringTheAttachAsync()
    {
        RegisterServices(isClubAdmin: true);
        Interop.GuardAttachGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interop.GuardAttachSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));

        // Entering the board starts the attach, which this gate holds open.
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => Interop.GuardAttachCount.ShouldBe(1));

        // Disposal lands while the boundary is still attaching. Cancellation cuts off the answer but not
        // the browser, so a disposal that only checked the attached flag would skip the detach and leave
        // the document holding a guard whose receiver it has already released, with no board left to
        // remove it. Waiting for the attach is what lets the lease be released on its real outcome.
        var board = cut.FindComponent<PlayerIntakeBoard>().Instance;
        var disposal = board.DisposeAsync().AsTask();
        Interop.GuardAttachGate.SetResult();
        // The attach's own completion is what marks the guard attached when the mounting is already gone,
        // so the assertion only means something once the boundary has finished installing it.
        await Interop.GuardAttachSettled!.Task;
        await disposal;

        Interop.GuardDetachCount.ShouldBe(1);
        Interop.GuardAttached.ShouldBeFalse();
    }

    /// <summary>A guard installed by an attach whose answer was lost is released on disposal.</summary>
    [Fact]
    public async Task PlayersReleasesAGuardInstalledByAnAttachThatLostItsAnswerAsync()
    {
        RegisterServices(isClubAdmin: true);
        Interop.FailAttachAfterInstalling = true;
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));

        // Entering the board starts the attach, which installs the guard and then loses its answer, so the
        // mounting never sees it as attached.
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => Interop.GuardAttached.ShouldBeTrue());

        // Disposal must release the lease it attempted rather than only the one it saw installed: the module
        // holds a guard whose receiver this mounting is about to release.
        await cut.FindComponent<PlayerIntakeBoard>().Instance.DisposeAsync();

        Interop.GuardAttached.ShouldBeFalse();
        Interop.GuardDetachCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    /// <summary>A replay is durably retained again before it is dispatched.</summary>
    [Fact]
    public async Task PlayersRetainsTheReplayedCommandAgainBeforeDispatchingItAsync()
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
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));
        Interop.WriteCount.ShouldBe(1);

        await cut.Find("#intake-submit").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => commands.Count.ShouldBe(2));
        commands[1].ShouldBeSameAs(commands[0]);
        // The record's presence is what makes a lost reply recoverable, so the second write is the
        // evidence that no replay is dispatched without one.
        Interop.WriteCount.ShouldBe(2);
    }

    /// <summary>A replay whose request cannot be retained again is not dispatched at all.</summary>
    [Fact]
    public async Task PlayersDispatchesNoReplayWhenTheRetainedRequestCannotBeWrittenAgainAsync()
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            commands.Add(call.Arg<CreatePlayerInput>());
            return Task.FromResult(new ServiceResult<PlayerCreationCompletion>(ServiceProblem.ServerError("Lost response")));
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));
        Interop.FailWrites = true;

        await cut.Find("#intake-submit").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));
        commands.Count.ShouldBe(1);
        cut.FindAll("#intake-unresolved").Count.ShouldBe(1);
        cut.Markup.ShouldContain("could not be retained safely");
    }

    /// <summary>A creation is abandoned when its page is re-scoped while the retained write runs.</summary>
    [Fact]
    public async Task PlayersDispatchesNoCreationWhoseIdentityChangedWhileTheRetainedWriteRanAsync()
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            commands.Add(call.Arg<CreatePlayerInput>());
            return Task.FromResult(new ServiceResult<PlayerCreationCompletion>(CreationCompletion(call.Arg<CreatePlayerInput>())));
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        Interop.WriteGate = new TaskCompletionSource();
        var submission = FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => Interop.WriteAttempts.ShouldBe(1));
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, clubId: 43)));
        await cut.InvokeAsync(() => Interop.WriteGate.SetResult());

        await submission;

        // Retaining crosses the browser boundary, so the club-42 command must not be dispatched with
        // the club-43 identity that replaced it, and the new page must keep its own empty state.
        commands.ShouldBeEmpty();
        cut.FindAll("#intake-receipt-heading").Count.ShouldBe(0);
        // The identity change moves the page back to the directory, so the abandoned submission
        // leaves no form behind for the new owner either.
        cut.FindAll("#intake-submit").Count.ShouldBe(0);
    }

    /// <summary>An addition whose set-aside cannot be released stays retained and recoverable.</summary>
    [Fact]
    public async Task PlayersKeepsTheRetainedAdditionWhenItsSetAsideCannotBeReleasedAsync()
    {
        var retained = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Taylor",
            LastName = "Lane",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        };
        Interop.Seed(new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(retained.OperationId, out var createdAt)
                ? createdAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow.AddHours(24),
            Payload = retained
        });
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));
        Interop.FailClears = true;

        await cut.Find("#intake-unresolved button").ClickAsync(new());
        await cut.Find("#set-aside-acknowledge").ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.Find("#intake-set-aside button.btn-warning").ClickAsync(new());

        // The decision is durable only once the bytes are gone, so a refused removal keeps the
        // addition in front of the member instead of reporting a set-aside storage did not record.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));
        cut.FindAll("#intake-unresolved").Count.ShouldBe(1);
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.Markup.ShouldContain("was not set aside");

        // A working boundary completes the same decision: retry storage, then set aside again.
        Interop.FailClears = false;
        await cut.Find("#intake-storage-unavailable button").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));
        await cut.Find("#intake-unresolved button").ClickAsync(new());
        await cut.Find("#set-aside-acknowledge").ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.Find("#intake-set-aside button.btn-warning").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(0));
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
    }

    /// <summary>A set-aside that succeeds directly clears the storage retry an earlier refusal asked for.</summary>
    [Fact]
    public async Task PlayersClearsTheStorageRetryWhenASetAsideSucceedsDirectlyAsync()
    {
        var retained = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Taylor",
            LastName = "Lane",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        };
        Interop.Seed(new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(retained.OperationId, out var createdAt)
                ? createdAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow.AddHours(24),
            Payload = retained
        });
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));
        Interop.FailClears = true;

        await cut.Find("#intake-unresolved button").ClickAsync(new());
        await cut.Find("#set-aside-acknowledge").ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.Find("#intake-set-aside button.btn-warning").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));

        // The same decision is taken again while storage answers, without the retry that refusal offered: the
        // removal itself is this page's proof that storage works, so the retry goes with the record it asked the
        // member to retry rather than outliving it.
        Interop.FailClears = false;
        await cut.Find("#intake-unresolved button").ClickAsync(new());
        await cut.Find("#set-aside-acknowledge").ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.Find("#intake-set-aside button.btn-warning").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(0));
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0);
        cut.Markup.ShouldNotContain("Retry storage");
    }

    /// <summary>A set-aside whose record another same-owner tab already removed completes the decision.</summary>
    [Fact]
    public async Task PlayersCompletesTheSetAsideWhenTheRetainedRecordWasAlreadyGoneAsync()
    {
        var retained = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Taylor",
            LastName = "Lane",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        };
        Interop.Seed(new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(retained.OperationId, out var createdAt)
                ? createdAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow.AddHours(24),
            Payload = retained
        });
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));

        // Another same-owner tab sets the same record aside first, so this board's removal finds nothing
        // left to remove while it still shows the addition it read.
        await Interop.ClearAsync(101, 42, retained.OperationId, CancellationToken.None);

        await cut.Find("#intake-unresolved button").ClickAsync(new());
        await cut.Find("#set-aside-acknowledge").ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.Find("#intake-set-aside button.btn-warning").ClickAsync(new());

        // The bytes are already gone, so the decision stands: reporting that the browser kept them would
        // hold the member on a removal that can never find the record again.
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Retained addition set aside"));
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0);
        cut.FindAll("#intake-unresolved").Count.ShouldBe(0);
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        Interop.Read(101, 42).ShouldBeNull();
    }

    /// <summary>The set-aside dialog states the settled refusal its branch describes.</summary>
    [Fact]
    public void PlayerIntakeBoardNamesTheRefusalInTheSetAsideDialog()
    {
        var cut = RenderRetainedAdditionBoard(refusedDuplicate: true);

        cut.Find("#intake-unresolved button").Click();

        // The outcome this decision follows is settled as a refusal, so the dialog asks the member to affirm
        // that rather than an unknown result the board never claimed.
        cut.Find("#intake-set-aside-heading").TextContent.ShouldContain("refused addition");
        // An inline confirmation is a live region with a name and a description, not a modal dialog.
        cut.Find("#intake-set-aside").GetAttribute("role").ShouldBe("status");
        cut.Find("#intake-set-aside").GetAttribute("aria-describedby").ShouldBe("intake-set-aside-consequence");
        cut.Find("#intake-set-aside-consequence").TextContent.ShouldContain("the refusal stands");
        var label = cut.Find("#intake-set-aside label").TextContent;
        label.ShouldContain("the refusal stands");
        label.ShouldNotContain("stays unknown");
    }

    /// <summary>An addition whose outcome is unknown keeps the unknown-result wording.</summary>
    [Fact]
    public void PlayerIntakeBoardKeepsTheUnknownWordingForAnUnsettledAddition()
    {
        var cut = RenderRetainedAdditionBoard(refusedDuplicate: false);

        cut.Find("#intake-unresolved button").Click();

        cut.Find("#intake-set-aside-heading").TextContent.ShouldContain("unresolved addition");
        cut.Find("#intake-set-aside label").TextContent.ShouldContain("stays unknown");
    }

    /// <summary>The set-aside result names the outcome its decision actually settled.</summary>
    [Fact]
    public void PlayersNamesTheSetAsideResultForTheOutcomeItSettled()
    {
        PlayersPage.RetainedSetAsideStatus(refusedDuplicate: true).ShouldContain("The refusal stands");
        PlayersPage.RetainedSetAsideStatus(refusedDuplicate: false).ShouldContain("stays unknown");
    }

    /// <summary>A settled receipt heads its panel even while the board still holds the retained command.</summary>
    [Fact]
    public void PlayerIntakeBoardHeadsASettledReceiptOverItsFrozenState()
    {
        var completion = CreationCompletion(new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Taylor",
            LastName = "Lane",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        });
        RegisterServices(isClubAdmin: true);
        var cut = Render<PlayerIntakeBoard>(p => p
            .Add(c => c.Heading, "Add player")
            .Add(c => c.SubmitLabel, "Create player")
            .Add(c => c.OwnerUserId, 101)
            .Add(c => c.ClubId, 42)
            .Add(c => c.CanManage, true)
            .Add(c => c.RecoveryChecked, true)
            .Add(c => c.Receipt, completion)
            .Add(c => c.RecoveryState, PlayerCreationRecoveryState.Unresolved));

        // The receipt panel is what renders, so the heading must not describe a frozen board whose fields, and
        // whose note about them, are not on screen.
        cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1);
        cut.FindAll("#intake-add-another").Count.ShouldBe(1);
        cut.Markup.ShouldNotContain("fields below are frozen");
    }

    /// <summary>A frozen board without a receipt still names why its fields are frozen.</summary>
    [Fact]
    public void PlayerIntakeBoardNamesTheFrozenStateWithoutAReceipt()
    {
        RegisterServices(isClubAdmin: true);
        var cut = Render<PlayerIntakeBoard>(p => p
            .Add(c => c.Heading, "Add player")
            .Add(c => c.SubmitLabel, "Create player")
            .Add(c => c.OwnerUserId, 101)
            .Add(c => c.ClubId, 42)
            .Add(c => c.CanManage, true)
            .Add(c => c.RecoveryChecked, true)
            .Add(c => c.RecoveryState, PlayerCreationRecoveryState.Unresolved));

        cut.FindAll("#intake-recovery-heading").Count.ShouldBe(1);
        cut.Markup.ShouldContain("fields below are frozen");
    }

    /// <summary>A frozen retained addition states no pre-commit campaign consequence.</summary>
    [Fact]
    public void PlayerIntakeBoardStatesNoConsequenceForAFrozenAddition()
    {
        RegisterServices(isClubAdmin: true);

        // The addition was already dispatched, so the campaign it enrolled in is the receipt's to state: while the
        // command is retained, the board names no consequence at all, because the Active campaign now is not
        // necessarily the one its completion will report.
        RenderConsequenceBoard(PlayerCreationRecoveryState.Unresolved).Markup.ShouldNotContain("Autumn campaign");

        // A form that has not been sent still states the consequence it commits under.
        RenderConsequenceBoard(PlayerCreationRecoveryState.None).Markup.ShouldContain("Autumn campaign");
    }

    /// <summary>Renders the create board with the intake consequence named, in the state given.</summary>
    /// <param name="state">The recovery state the board is in.</param>
    /// <param name="intakeContextLoading">Whether the enrollment-consequence read is still in flight.</param>
    /// <param name="intakeContextUnavailable">Whether the enrollment consequence could not be read.</param>
    /// <returns>The rendered board.</returns>
    private IRenderedComponent<PlayerIntakeBoard> RenderConsequenceBoard(
        PlayerCreationRecoveryState state,
        bool intakeContextLoading = false,
        bool intakeContextUnavailable = false)
        => Render<PlayerIntakeBoard>(p => p
            .Add(c => c.Heading, "Add player")
            .Add(c => c.SubmitLabel, "Create player")
            .Add(c => c.OwnerUserId, 101)
            .Add(c => c.ClubId, 42)
            .Add(c => c.CanManage, true)
            .Add(c => c.RecoveryChecked, true)
            .Add(c => c.ShowsEnrollmentConsequence, true)
            .Add(c => c.IntakeContextLoading, intakeContextLoading)
            .Add(c => c.IntakeContextUnavailable, intakeContextUnavailable)
            .Add(c => c.IntakeContext, new PlayerIntakeContext { CampaignId = 4, CampaignName = "Autumn campaign" })
            .Add(c => c.RecoveryState, state));

    /// <summary>A new addition waits for the enrollment consequence the board names as unread.</summary>
    [Fact]
    public void PlayerIntakeBoardWithholdsCommitWhileTheEnrollmentConsequenceIsUnread()
    {
        RegisterServices(isClubAdmin: true);

        // The board says the consequence is still being read, so committing under it would enroll the player
        // in a campaign nobody has read: the control stays shut until the read settles.
        var loading = RenderConsequenceBoard(PlayerCreationRecoveryState.None, intakeContextLoading: true);
        loading.Find("p.intake-consequence").TextContent.ShouldContain("Checking the enrollment consequence");
        loading.Find("#intake-submit").HasAttribute("disabled").ShouldBeTrue();

        // A read that failed is settled, and the board says what that means: the member decides deliberately
        // and the real result is reported after the submission instead of a campaign fact nobody read.
        var unread = RenderConsequenceBoard(PlayerCreationRecoveryState.None, intakeContextUnavailable: true);
        unread.Find("p.intake-consequence").TextContent.ShouldContain("could not be read");
        unread.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse();
    }

    /// <summary>A same-owner refresh replaces the form and releases the departure question with it.</summary>
    [Fact]
    public async Task PlayersClearsDepartureStateWhenTheIdentityRefreshesAsync()
    {
        RegisterServices(isClubAdmin: true);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("#player-first-name").HasAttribute("disabled").ShouldBeFalse());

        // The member types and asks to leave, then a role-only refresh lands while the question is open.
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Taylor" });
        await cut.Find("#intake-cancel").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-departure").Count.ShouldBe(1));
        await cut.WaitForAssertionAsync(() => Interop.Dirty.ShouldBeTrue());

        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(false)));

        // The refresh replaced the form, so neither the question nor the guard's dirty state may keep speaking
        // for input the member no longer has on screen.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-departure").Count.ShouldBe(0));
        await cut.WaitForAssertionAsync(() => Interop.Dirty.ShouldBeFalse());
    }

    /// <summary>Renders the board in the state its set-aside decision is made in.</summary>
    /// <param name="refusedDuplicate">Whether a receipt-backed refusal is the outcome it published.</param>
    /// <returns>The rendered board.</returns>
    private IRenderedComponent<PlayerIntakeBoard> RenderRetainedAdditionBoard(bool refusedDuplicate)
    {
        RegisterServices(isClubAdmin: true);
        return Render<PlayerIntakeBoard>(p => p
            .Add(c => c.Heading, "Add player")
            .Add(c => c.SubmitLabel, "Create player")
            .Add(c => c.OwnerUserId, 101)
            .Add(c => c.ClubId, 42)
            .Add(c => c.CanManage, true)
            .Add(c => c.RecoveryChecked, true)
            .Add(c => c.RecoveryState, PlayerCreationRecoveryState.Unresolved)
            .Add(c => c.Duplicate, refusedDuplicate
                ? new PlayerCreationDuplicate { PlayerId = 21, LifecycleStatus = LifecycleStatus.Active }
                : null));
    }

    /// <summary>A replay is not offered while the set-aside decision that owns the command is open.</summary>
    [Fact]
    public async Task PlayersKeepsTheReplayUnavailableWhileTheSetAsideDecisionIsOpenAsync()
    {
        var retained = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Taylor",
            LastName = "Lane",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        };
        Interop.Seed(new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(retained.OperationId, out var createdAt)
                ? createdAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow.AddHours(24),
            Payload = retained
        });
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));

        // The retained command is replayable, and that one control is what settles it.
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#intake-submit").TextContent.ShouldContain("Replay the retained addition");

        // Opening the decision suspends it: the member is deciding whether to abandon that very command, so a
        // replay begun beside the decision would settle the operation under it.
        await cut.Find("#intake-unresolved button").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-set-aside").Count.ShouldBe(1));
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#intake-submit").TextContent.ShouldNotContain("Replay the retained addition");

        // Keeping it retained hands the one control back, so the suspension is never a dead end.
        await cut.Find("#intake-set-aside button.btn-outline-secondary").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-set-aside").Count.ShouldBe(0));
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#intake-submit").TextContent.ShouldContain("Replay the retained addition");
    }

    /// <summary>Correcting a field drops the server message that described the value it refused.</summary>
    [Fact]
    public async Task PlayersDropsAServerFieldMessageWhenItsFieldIsCorrectedAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                ServiceProblem.Validation(nameof(PlayerProfileInput.FirstName), "Use the name on the register."))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Use the name on the register."));
        cut.Find("#player-first-name").GetAttribute("class")!.ShouldContain("is-invalid");

        // The server answered about the value that was sent, so the correction takes the message and the invalid
        // class with it rather than leaving them describing a value the member has already replaced.
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Corrected" });

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldNotContain("Use the name on the register."));
        cut.Find("#player-first-name").GetAttribute("class")!.ShouldNotContain("is-invalid");
    }

    /// <summary>A server field error keyed to Gender renders beside its own control.</summary>
    [Fact]
    public async Task PlayersRendersAGenderFieldErrorBesideItsControlAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                ServiceProblem.Validation(nameof(PlayerProfileInput.Gender), "Choose a listed gender."))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);

        // Every other profiled field renders its server messages beside its control; Gender must too, or a
        // server error keyed to it is omitted from the per-field feedback contract. The invalid state is
        // carried by the class, because these form components own aria-invalid and report only the
        // validation their own edit context holds, while the message the server keyed to this field is
        // named by the attribute the control carries.
        await service.Received(1).CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Choose a listed gender."));
        cut.Find("#player-gender").ParentElement!.TextContent.ShouldContain("Choose a listed gender.");
        cut.Find("#player-gender").GetAttribute("class").ShouldNotBeNull().ShouldContain("is-invalid");
        cut.Find("#player-gender").GetAttribute("aria-describedby").ShouldBe("player-gender-error");
        cut.Find("#player-gender-error").TextContent.ShouldContain("Choose a listed gender.");
    }

    /// <summary>A field with server feedback names the region holding that message.</summary>
    [Fact]
    public async Task PlayersDescribesAServerFieldMessageFromItsControlAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                ServiceProblem.Validation(nameof(PlayerProfileInput.FirstName), "Use the name the club records."))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse());

        // Nothing to describe yet, so the field names no region it does not have.
        cut.Find("#player-first-name").HasAttribute("aria-describedby").ShouldBeFalse();

        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Taylor" });
        await cut.Find("#player-last-name").ChangeAsync(new() { Value = "Lane" });
        await cut.Find("#intake-submit").ClickAsync(new());

        // The control that carries the refusal names the region its message renders in, so the reason is
        // available where the field is marked, rather than only as text elsewhere on the page. The mark is
        // the class: the form component reports only the validation its own edit context holds, so a
        // message the server keyed to this field never reaches its aria-invalid.
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Use the name the club records."));
        cut.Find("#player-first-name").GetAttribute("class").ShouldNotBeNull().ShouldContain("is-invalid");
        cut.Find("#player-first-name").GetAttribute("aria-describedby").ShouldBe("player-first-name-error");
        cut.Find("#player-first-name-error").TextContent.ShouldContain("Use the name the club records.");

        // Focus follows the refusal to the field the member has to correct.
        await cut.WaitForAssertionAsync(() => Interop.FocusRegions.ShouldContain(region => region == ".intake-fields .is-invalid"));
    }

    /// <summary>A message the form produced before any request is described the same way.</summary>
    [Fact]
    public async Task PlayersDescribesItsOwnValidationMessageFromItsControlAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse());

        // An untouched form is refused before any request is made, so the messages the browser produced
        // are the only feedback there is — and the control still points at them.
        await cut.Find("#intake-submit").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.Find("#player-first-name")
            .GetAttribute("aria-describedby").ShouldBe("player-first-name-error"));
        // This refusal is the edit context's own, so the form component reports it on the control itself.
        cut.Find("#player-first-name").GetAttribute("aria-invalid").ShouldBe("true");
        cut.Find("#player-first-name-error").TextContent.ShouldNotBeNullOrWhiteSpace();
        await service.DidNotReceive().CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>());

        // The refusal also moves focus to the field to correct, which the surface's contract asks for.
        Interop.FocusRegions.ShouldContain(region => region == ".intake-fields .is-invalid");
    }

    /// <summary>Focus moves to the first field to correct once per refusal, not after every correction.</summary>
    [Fact]
    public async Task PlayersMovesFocusToTheFirstFieldNeedingCorrectionOncePerRefusalAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse());

        // The refusal moves focus once, to the first field the member has to correct.
        await cut.Find("#intake-submit").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => Interop.FocusRegions.Count.ShouldBe(1));
        Interop.FocusRegions[0].ShouldBe(".intake-fields .is-invalid");

        // Correcting that field leaves the others refusing, which is the same refusal: focus stays where the
        // member put it rather than being dragged to the next message the corrections clear.
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Taylor" });
        await cut.Find("#player-last-name").ChangeAsync(new() { Value = "Lane" });

        Interop.FocusRegions.Count.ShouldBe(1);
    }

    /// <summary>A frozen refusal still asks focus to follow the feedback it left.</summary>
    [Fact]
    public async Task PlayersAsksFocusToFollowTheFeedbackAFrozenRefusalLeftAsync()
    {
        var commands = new List<CreatePlayerInput>();
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var input = call.Arg<CreatePlayerInput>();
            commands.Add(input);
            // The first attempt's outcome is unknown, so the board holds it frozen; the replayed command is
            // then refused with field feedback.
            ServiceResult<PlayerCreationCompletion> result = commands.Count == 1
                ? ServiceProblem.ServerError("Lost acknowledgement")
                : ServiceProblem.Validation(nameof(PlayerFormState.FirstName), "Unexpected validation response");
            return Task.FromResult(result);
        });
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));

        await FillAndSubmitAsync(cut);

        // The replayed command is refused with field feedback while the board still holds the unresolved
        // attempt, which is the refusal that has to move focus.
        await cut.Find("#intake-submit").ClickAsync(new());

        // The fields stay closed — a validation response cannot settle the retained attempt — and the board
        // still asks focus to follow the feedback. The control it names cannot take that focus while the
        // fieldset is disabled, so the module reaches the message's own region instead; that half of the
        // contract is pinned in the browser by the module's focus case.
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue());
        cut.Find("#player-first-name-error").TextContent.ShouldContain("Unexpected validation response");
        await cut.WaitForAssertionAsync(() => Interop.FocusRegions.ShouldContain(region => region == ".intake-fields .is-invalid"));
    }

    /// <summary>A refused operation whose bytes cannot be released stays blocked with the retry.</summary>
    [Fact]
    public async Task PlayersKeepsARefusedOperationBlockedWhenItsBytesCannotBeReleasedAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                PlayerCreationProblems.Duplicate(call.Arg<CreatePlayerInput>().OperationId, 21, LifecycleStatus.Active))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        Interop.FailClears = true;

        await FillAndSubmitAsync(cut);

        // The refusal stands, but the browser still holds the exact request: the board keeps the
        // operation blocked and offers the retry instead of releasing it in name only.
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("already exists"));
        cut.FindAll("#intake-unresolved").Count.ShouldBe(1);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));

        // The receipt-backed refusal settled this addition's outcome, so the panel names it instead of claiming
        // the addition could not be checked: what remains is the browser's copy of the request.
        var notice = cut.Find("#intake-storage-unavailable").TextContent;
        notice.ShouldContain("refused addition");
        notice.ShouldContain("The refusal above stands");
        notice.ShouldNotContain("could not be checked");

        // A working boundary can still complete the release, so the blocked state is recoverable.
        Interop.FailClears = false;
        await cut.Find("#intake-storage-unavailable button").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));
        await cut.Find("#intake-unresolved button").ClickAsync(new());
        await cut.Find("#set-aside-acknowledge").ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.Find("#intake-set-aside button.btn-warning").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(0));
    }

    /// <summary>A release that finished after a role refresh does not disturb the preserved work.</summary>
    [Fact]
    public async Task PlayersIgnoresAReleaseThatFinishedAfterTheIdentityRefreshedAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                PlayerCreationProblems.Duplicate(call.Arg<CreatePlayerInput>().OperationId, 21, LifecycleStatus.Active))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        Interop.FailClears = true;
        Interop.ClearGate = new TaskCompletionSource();

        var submission = FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => Interop.ClearAttempts.ShouldBe(1));

        // A role-only refresh preserves this owner's retained addition, and the held read keeps the
        // restored state observable instead of letting a later read land it again.
        Interop.ReadGate = new TaskCompletionSource();
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(false)));
        var readsBeforeTheRelease = Interop.ReadCount;
        await cut.InvokeAsync(() => Interop.ClearGate.SetResult());
        await submission;

        // The stale continuation belongs to the identity that dispatched the command, so it must
        // neither erase the preserved state nor report another club's refusal, and it must not spend a
        // boundary read of its own: the page now on screen owns its reads.
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0);
        cut.FindAll("#intake-duplicate").Count.ShouldBe(0);
        Interop.ReadCount.ShouldBe(readsBeforeTheRelease);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));
    }

    /// <summary>A commit that released after a role refresh does not close the guard.</summary>
    [Fact]
    public async Task PlayersDoesNotCloseTheGuardWhenTheCommitSettlesAfterAnIdentityRefreshAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                CreationCompletion(call.Arg<CreatePlayerInput>()))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        Interop.FailClears = true;
        Interop.ClearGate = new TaskCompletionSource();

        var submission = FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Enrolled in Original campaign."));
        await cut.WaitForAssertionAsync(() => Interop.ClearAttempts.ShouldBe(1));

        Interop.ReadGate = new TaskCompletionSource();
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(false)));
        await cut.InvokeAsync(() => Interop.ClearGate.SetResult());
        await submission;

        // Settlement continues across the release, so a refresh that lands mid-release must stop it:
        // no storage claim and no receipt focus for a form that no longer holds this receipt.
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0);
    }

    /// <summary>A settled receipt still reports an unreleased record and offers the retry.</summary>
    [Fact]
    public async Task PlayersKeepsTheStorageRetryVisibleBesideASettledReceiptAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                CreationCompletion(call.Arg<CreatePlayerInput>()))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        Interop.FailClears = true;

        await FillAndSubmitAsync(cut);

        // The receipt proves the creation, but the browser kept its retained request. Suppressing the
        // storage status behind the receipt would hide the reason it comes back as unresolved later,
        // so the panel and its retry stay visible beside the receipt.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));
        cut.Find("#intake-storage-unavailable").TextContent.ShouldContain("was not released");

        // A working boundary releases it: the action clears the exact settled record, so it cannot
        // return as an unresolved addition on the next mount.
        Interop.FailClears = false;
        await cut.Find("#intake-storage-unavailable button").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0));
        Interop.Read(101, 42).ShouldBeNull();
        cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1);
    }

    /// <summary>A receipt whose request is still unreleased survives the visit that produced it.</summary>
    [Fact]
    public async Task PlayersKeepsTheUnreleasedReceiptAcrossAVisitAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                CreationCompletion(call.Arg<CreatePlayerInput>()))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        Interop.FailClears = true;

        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));
        Interop.Read(101, 42).ShouldNotBeNull();

        // The member leaves and comes back before the browser released the request, and the recovery read is
        // unavailable on the return: the receipt is the only evidence of the committed operation, so it comes
        // back with the retry that releases it rather than being dropped with the visit.
        Interop.FailReads = true;
        await cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo("/players"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));

        var board = cut.FindComponent<PlayerIntakeBoard>().Instance;
        board.Receipt.ShouldNotBeNull();
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1));
        cut.Markup.ShouldContain("Enrolled in Original campaign.");
        // Releasing it succeeds now, and the receipt stays where the operation's evidence belongs.
        Interop.FailClears = false;
        await cut.Find("#intake-storage-unavailable button").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0));
        cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1);
        Interop.Read(101, 42).ShouldBeNull();
    }

    /// <summary>A dirty sync that teardown cancels is handled rather than left faulting a detached task.</summary>
    [Fact]
    public async Task PlayerIntakeBoardTreatsACancelledDirtySyncAsTeardownAsync()
    {
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => Interop.GuardAttached.ShouldBeTrue());
        // The continuation that records the attach on the board runs after the boundary's answer, so the test
        // drains a dispatch before calling the sync the board guards on.
        await cut.InvokeAsync(() => Task.CompletedTask);

        Interop.FailDirtyWithCancellation = true;
        var board = cut.FindComponent<PlayerIntakeBoard>().Instance;
        var attempts = Interop.DirtyAttempts;

        await cut.InvokeAsync(() => board.SyncDirtyAsync());
        Interop.DirtyAttempts.ShouldBe(attempts + 1);

        // The field-change handler starts this detached from any caller, so a teardown cancellation inside it must
        // be handled here rather than faulting the task nobody observes.
        await Should.NotThrowAsync(() => cut.InvokeAsync(() => board.SyncDirtyAsync()));
    }

    /// <summary>The receipt's actions stay closed while the settlement that owns the record is still running.</summary>
    [Fact]
    public void PlayerIntakeBoardClosesTheReceiptActionsWhileTheSettlementRuns()
    {
        var completion = CreationCompletion(new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Taylor",
            LastName = "Lane",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        });
        RegisterServices(isClubAdmin: true);

        // The submit state stays set until the settlement's storage cleanup has finished, so the next addition
        // cannot start over the record that settlement still owns.
        var cut = Render<PlayerIntakeBoard>(p => p
            .Add(c => c.Heading, "Add player")
            .Add(c => c.SubmitLabel, "Create player")
            .Add(c => c.OwnerUserId, 101)
            .Add(c => c.ClubId, 42)
            .Add(c => c.CanManage, true)
            .Add(c => c.RecoveryChecked, true)
            .Add(c => c.Receipt, completion)
            .Add(c => c.IsSubmitting, true));
        cut.Find("#intake-add-another").HasAttribute("disabled").ShouldBeTrue();

        // Once the settlement has finished, the same board offers the next addition again.
        cut.Render(p => p
            .Add(c => c.Heading, "Add player")
            .Add(c => c.SubmitLabel, "Create player")
            .Add(c => c.OwnerUserId, 101)
            .Add(c => c.ClubId, 42)
            .Add(c => c.CanManage, true)
            .Add(c => c.RecoveryChecked, true)
            .Add(c => c.Receipt, completion)
            .Add(c => c.IsSubmitting, false));
        cut.Find("#intake-add-another").HasAttribute("disabled").ShouldBeFalse();
    }

    /// <summary>A release retry whose record another same-owner tab already removed reports the release.</summary>
    [Fact]
    public async Task PlayersReleasesTheReceiptRetryWhenTheRetainedRecordWasAlreadyGoneAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                CreationCompletion(call.Arg<CreatePlayerInput>()))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        Interop.FailClears = true;

        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));
        cut.Find("#intake-add-another").HasAttribute("disabled").ShouldBeTrue();

        // Another same-owner tab releases the record this receipt could not, so the retry finds nothing
        // left to remove.
        var retained = Interop.Read(101, 42);
        retained.ShouldNotBeNull();
        Interop.FailClears = false;
        await Interop.ClearAsync(101, 42, retained.Payload.OperationId, CancellationToken.None);

        await cut.Find("#intake-storage-unavailable button").ClickAsync(new());

        // The record this receipt settled is provably gone, so the retry reports the release it asked for
        // instead of leaving the member on a retry that can never find the record again.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0));
        cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1);
        await cut.WaitForAssertionAsync(() => cut.Find("#intake-add-another").HasAttribute("disabled").ShouldBeFalse());
    }

    /// <summary>A release retry reports the release when another operation's record replaced this one's.</summary>
    [Fact]
    public async Task PlayersReportsTheReleaseWhenAnotherOperationReplacedTheRetainedRecordAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                CreationCompletion(call.Arg<CreatePlayerInput>()))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        Interop.FailClears = true;

        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));

        // Another same-owner tab releases this record and retains its own addition in its place, which is
        // the only way a second record can exist for one owner.
        var replacement = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Robin",
            LastName = "Vale",
            DateOfBirth = new DateOnly(2011, 3, 2),
            GraduationYear = 2030
        };
        var retained = Interop.Read(101, 42);
        retained.ShouldNotBeNull();
        Interop.FailClears = false;
        await Interop.ClearAsync(101, 42, retained.Payload.OperationId, CancellationToken.None);
        await Interop.WriteAsync(new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(replacement.OperationId, out var replacedAt)
                ? replacedAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow.AddHours(24),
            Payload = replacement
        }, CancellationToken.None);

        await cut.Find("#intake-storage-unavailable button").ClickAsync(new());

        // One record per owner means a record naming another operation proves this receipt's is gone rather
        // than kept, and the other tab's record is left exactly where it is.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0));
        Interop.Read(101, 42)!.Payload.OperationId.ShouldBe(replacement.OperationId);
    }

    /// <summary>A retention failure that belongs to a replaced identity is not published.</summary>
    [Fact]
    public async Task PlayersDoesNotPublishAStaleRetentionFailureIntoTheNewIdentityAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                CreationCompletion(call.Arg<CreatePlayerInput>()))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        Interop.WriteGate = new TaskCompletionSource();

        var submission = FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => Interop.WriteAttempts.ShouldBe(1));

        // The page is refreshed while the retained write is still open, and the write then fails.
        Interop.ReadGate = new TaskCompletionSource();
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(false)));
        Interop.FailWrites = true;
        await cut.InvokeAsync(() => Interop.WriteGate.SetResult());
        await submission;

        // The failure belongs to the identity that asked for the write, so the page now on screen is
        // neither told about it nor left holding a submission it did not start.
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(0);
        cut.FindAll("#intake-error").Count.ShouldBe(0);
        await service.DidNotReceive().CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A release retry that finishes after a refresh does not clear the new storage report.</summary>
    [Fact]
    public async Task PlayersDoesNotLetAStaleReleaseClearTheRefreshedStorageReportAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                CreationCompletion(call.Arg<CreatePlayerInput>()))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        Interop.FailClears = true;

        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));

        // The release is retried and stays open while the page is refreshed and storage then breaks
        // for the identity on screen, which reports that as its own state.
        Interop.ClearGate = new TaskCompletionSource();
        Interop.FailClears = false;
        var release = cut.Find("#intake-storage-unavailable button").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => Interop.ClearAttempts.ShouldBe(2));
        Interop.FailReads = true;
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(false)));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));

        await cut.InvokeAsync(() => Interop.ClearGate.SetResult());
        await release;

        // The late continuation belongs to the operation it released, so it neither clears the newer
        // report nor claims a storage state for a page it no longer owns.
        cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1);
    }

    /// <summary>A same-owner refresh keeps the settled receipt and the release retry it left.</summary>
    [Fact]
    public async Task PlayersPreservesTheSettledReceiptAndRetryAcrossARoleRefreshAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                CreationCompletion(call.Arg<CreatePlayerInput>()))));
        RegisterServices(isClubAdmin: true, managementService: service);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        Interop.FailClears = true;

        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));

        // A role-only refresh is the same owner's intake: it must not replace the settled receipt (and
        // the actions it carries) with a blank form, nor hide the record it could not release. The
        // refresh's own read fails here, which is the state the member then sees.
        Interop.FailReads = true;
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(false)));

        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1));
        cut.FindAll("#intake-add-another").Count.ShouldBe(1);
        // The record this receipt owns was never released, and the browser's own reservation refuses a
        // second command for the owner until it is, so the flow that starts another one is not offered:
        // pressing it would drop the receipt and read the committed command back as unresolved.
        cut.Find("#intake-add-another").HasAttribute("disabled").ShouldBeTrue();
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));
    }

    /// <summary>Discarding typed input on departure leaves nothing for a later visit to resurrect.</summary>
    [Fact]
    public async Task PlayersDiscardsTypedValuesWhenTheDepartureIsConfirmedAsync()
    {
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Taylor" });
        await cut.WaitForAssertionAsync(() => Interop.GuardAttached.ShouldBeTrue());

        // The module asks before a same-origin departure; confirming it is an explicit discard.
        await cut.InvokeAsync(() => cut.FindComponent<PlayerIntakeBoard>().Instance
            .OnBoardDepartureAttemptAsync(Interop.GuardLease!, "/players"));
        await cut.Find("#intake-departure button.btn-warning").ClickAsync(new());

        // Returning to the board shows nothing: the confirmation said the details would be lost.
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("#player-first-name").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#player-first-name").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    /// <summary>Cancel asks before it discards what the member typed.</summary>
    [Fact]
    public async Task PlayersAsksBeforeCancelDiscardsTypedInputAsync()
    {
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("#player-first-name").HasAttribute("disabled").ShouldBeFalse());
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Guardian" });

        await cut.Find("#intake-cancel").ClickAsync(new());

        // Cancel is a departure like any other: the typed value is not the click's to lose, so the panel the
        // guard opens asks first and the member stays on the form while they decide.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-departure").Count.ShouldBe(1));
        cut.Find("#intake-departure").GetAttribute("role").ShouldBe("status");
        cut.Find("#intake-departure").GetAttribute("aria-describedby").ShouldBe("intake-departure-consequence");
        cut.Find("#intake-departure-consequence").TextContent.ShouldContain("will be lost");
        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/players/new");

        await cut.Find("#intake-departure button.btn-outline-secondary").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-departure").Count.ShouldBe(0));
        cut.Find("#player-first-name").GetAttribute("value").ShouldBe("Guardian");

        // Only the confirmed departure discards it, and it leaves nothing for a later visit to resurrect.
        await cut.Find("#intake-cancel").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-departure").Count.ShouldBe(1));
        await cut.Find("#intake-departure button.btn-warning").ClickAsync(new());
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("#player-first-name").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#player-first-name").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    /// <summary>Cancel with nothing typed leaves at once, because it can lose nothing.</summary>
    [Fact]
    public async Task PlayersCancelsImmediatelyWhenNothingWasTypedAsync()
    {
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("#player-first-name").HasAttribute("disabled").ShouldBeFalse());

        await cut.Find("#intake-cancel").ClickAsync(new());

        cut.FindAll("#intake-departure").Count.ShouldBe(0);
        await cut.WaitForAssertionAsync(() =>
            Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/players"));
    }

    /// <summary>Cancel asks about an acknowledged set-aside decision, whose checkbox is outside the form.</summary>
    [Fact]
    public async Task PlayersAsksBeforeCancelDiscardsAnAcknowledgedSetAsideDecisionAsync()
    {
        var retained = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Taylor",
            LastName = "Lane",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        };
        Interop.Seed(new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(retained.OperationId, out var createdAt)
                ? createdAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow.AddHours(24),
            Payload = retained
        });
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));

        // The acknowledgement never reaches the edit context, so nothing is typed and the board's own dirty flag
        // stays clear: the decision is what Cancel has to ask about, exactly as a departing link does.
        await cut.Find("#intake-unresolved button").ClickAsync(new());
        await cut.Find("#set-aside-acknowledge").ChangeAsync(new ChangeEventArgs { Value = true });
        Interop.Dirty.ShouldBeFalse();

        await cut.Find("#intake-cancel").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-departure").Count.ShouldBe(1));
        Services.GetRequiredService<NavigationManager>().Uri.ShouldEndWith("/players/new");
    }

    /// <summary>Another addition reads the enrollment consequence again before it offers the fields.</summary>
    [Fact]
    public async Task PlayersReloadsTheEnrollmentConsequenceForAnotherAdditionAsync()
    {
        var service = Substitute.For<IPlayerManagementService>();
        service.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new ServiceResult<PlayerCreationCompletion>(
                CreationCompletion(call.Arg<CreatePlayerInput>()))));
        var context = Substitute.For<IPlayerIntakeContextService>();
        var campaignName = "Original campaign";
        context.GetPlayerIntakeContextAsync(Arg.Any<GetPlayerIntakeContextInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ServiceResult<PlayerIntakeContext>(new PlayerIntakeContext
            {
                CampaignId = 4,
                CampaignName = campaignName
            })));
        RegisterServices(isClubAdmin: true, managementService: service, intakeContextService: context);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await FillAndSubmitAsync(cut);
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-receipt-heading").Count.ShouldBe(1));

        // Another tab opens a new campaign while the receipt is on screen, so the next addition must state what
        // is Active now rather than the campaign the receipt was enrolled in.
        campaignName = "Autumn campaign";
        await cut.Find("#intake-add-another").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Autumn campaign"));
        cut.Markup.ShouldNotContain("Original campaign");
    }

    /// <summary>Both confirmations move focus into the panel they open.</summary>
    [Fact]
    public async Task PlayersMovesFocusIntoEachConfirmationPanelAsync()
    {
        var retained = new CreatePlayerInput
        {
            OperationId = Guid.CreateVersion7(),
            ClubId = 42,
            FirstName = "Taylor",
            LastName = "Lane",
            DateOfBirth = new DateOnly(2012, 5, 1),
            GraduationYear = 2031
        };
        Interop.Seed(new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(retained.OperationId, out var createdAt)
                ? createdAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow.AddHours(24),
            Payload = retained
        });
        RegisterServices(isClubAdmin: true);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-unresolved").Count.ShouldBe(1));

        // The set-aside confirmation is the next step, so focus must land in it.
        await cut.Find("#intake-unresolved button").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => Interop.FocusRegions.ShouldContain(region => region == "#intake-set-aside-heading"));

        // As must the departure panel, whose own actions are what the member now needs.
        await cut.InvokeAsync(() => cut.FindComponent<PlayerIntakeBoard>().Instance
            .OnBoardDepartureAttemptAsync(Interop.GuardLease!, "/players"));
        await cut.WaitForAssertionAsync(() => Interop.FocusRegions.ShouldContain(region => region == "#intake-departure-heading"));
    }

    /// <summary>A same-owner refresh re-arms the withhold gates before its replacement reads.</summary>
    [Fact]
    public async Task PlayersWithholdsTheBoardWhileASameOwnerRefreshReReadsAsync()
    {
        RegisterServices(isClubAdmin: true);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.WaitForAssertionAsync(() => cut.Find("#player-first-name").HasAttribute("disabled").ShouldBeFalse());

        Interop.ReadGate = new TaskCompletionSource();
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(false)));

        // The replacement read is open, so the board must refuse input and name the check rather than
        // let a landed command or campaign result land over what the member types meanwhile.
        await cut.WaitForAssertionAsync(() => cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue());
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-checking-note").Count.ShouldBe(1));
    }

    private async Task FillAndSubmitAsync(IRenderedComponent<PlayersPage> cut)
    {
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Taylor" });
        await cut.Find("#player-last-name").ChangeAsync(new() { Value = "Lane" });
        await cut.Find("#intake-submit").ClickAsync(new());
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
