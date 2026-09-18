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
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));

        cut.Markup.ShouldNotContain("View existing player");
        cut.FindComponent<PlayerIntakeBoard>().Instance.Duplicate.ShouldBeNull();
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        await cut.Find("#player-first-name").ChangeAsync(new() { Value = "Another" });
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

    /// <summary>A failed storage read still settles the check, so a well-formed board is never stuck shut.</summary>
    [Fact]
    public async Task PlayersReopensTheBoardWhenTheRetainedCommandReadFailsAsync()
    {
        RegisterServices(isClubAdmin: true);
        Interop.FailReads = true;
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => FollowDirectoryLink(cut, "a.btn-primary"));

        // The board may speak only for itself: storage was not checked, and nothing left this board.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#intake-storage-unavailable").Count.ShouldBe(1));
        cut.Find("#intake-storage-unavailable").TextContent.ShouldContain("Nothing has been sent from this board.");
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeFalse();
        cut.Find("#intake-submit").HasAttribute("disabled").ShouldBeFalse();
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

        await cut.InvokeAsync(() => context.SetResult(new ServiceResult<PlayerIntakeContext>(IntakeContext)));
        await cut.WaitForAssertionAsync(() => cut.Find("p.intake-consequence").TextContent.ShouldContain("Summer Tryouts"));
        cut.Markup.ShouldNotContain("Checking the enrollment consequence");
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
