using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Nova.SharedKernel.Features.Players;
using Nova.UI.Features.Players.Services;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests the in-memory stand-in's storage contract, which mirrors the collocated module's: retained bytes
/// are owner-scoped, and one operation identity carries one exact command.
/// </summary>
public sealed class PlayerIntakeInteropDoubleTests
{
    /// <summary>A write under an existing operation identity may only carry the retained bytes back.</summary>
    [Fact]
    public async Task WriteAsyncRefusesDifferentBytesUnderTheSameOperationAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var operationId = Guid.CreateVersion7();
        var retained = new PendingPlayerCreation
        {
            ActorUserId = 101,
            RecoveryExpiresAt = PlayerCreationOperation.TryGetCreatedAt(operationId, out var createdAt)
                ? createdAt.Add(PlayerCreationOperation.Lifetime)
                : DateTimeOffset.UtcNow.AddHours(24),
            Payload = new CreatePlayerInput
            {
                OperationId = operationId,
                ClubId = 42,
                FirstName = "Taylor",
                LastName = "Lane",
                DateOfBirth = new DateOnly(2012, 5, 1),
                GraduationYear = 2031
            }
        };
        var interop = new PlayerIntakeInteropDouble();
        await interop.WriteAsync(retained, cancellationToken);
        interop.WriteCount.ShouldBe(1);

        // The same operation identity with different bytes is refused rather than replacing the command the
        // member's dispatch is accounted for by, exactly as the module refuses it.
        var altered = retained with { Payload = retained.Payload with { FirstName = "Altered" } };
        await Should.ThrowAsync<JSException>(() => interop.WriteAsync(altered, cancellationToken));

        // The retained command is left exactly as it was.
        interop.WriteCount.ShouldBe(1);
        var read = await interop.ReadAsync(101, 42, cancellationToken);
        read.Kind.ShouldBe(PlayerCreationRecoveryKind.Pending);
        read.Pending!.Payload.FirstName.ShouldBe("Taylor");
    }

    /// <summary>A dirty update from a mounting the guard has left is ignored, exactly as the module ignores it.</summary>
    [Fact]
    public async Task MarkDirtyAsyncIgnoresALeaseThatDoesNotOwnTheGuardAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var interop = new PlayerIntakeInteropDouble();
        await interop.AttachDepartureGuardAsync(new ElementReference(), new object(), "current-lease", cancellationToken);
        interop.GuardAttached.ShouldBeTrue();

        await interop.MarkDirtyAsync("current-lease", true, cancellationToken);
        interop.Dirty.ShouldBeTrue();

        // A completion from a superseded mounting cannot speak for the guard the board on screen owns.
        await interop.MarkDirtyAsync("stale-lease", false, cancellationToken);
        interop.Dirty.ShouldBeTrue();

        // A detached guard owns nothing either, which is the module's null active guard.
        await interop.DetachDepartureGuardAsync("current-lease", cancellationToken);
        await interop.MarkDirtyAsync("current-lease", false, cancellationToken);
        interop.Dirty.ShouldBeTrue();
    }
}
