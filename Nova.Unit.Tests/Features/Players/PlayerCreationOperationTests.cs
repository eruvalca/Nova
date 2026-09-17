using Nova.SharedKernel.Features.Players;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

public sealed class PlayerCreationOperationTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void DeadlineIsExclusiveAndDerivedFromOperationTimestamp(int ticks, bool valid)
    {
        var issued = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
        var operation = Guid.CreateVersion7(issued);
        PlayerCreationOperation.TryGetDeadline(operation, issued.AddHours(24).AddTicks(ticks), out var deadline).ShouldBe(valid);
        deadline.ShouldBe(issued.AddHours(24));
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(59999, true)]
    [InlineData(60000, true)]
    [InlineData(60001, false)]
    public void FutureClockToleranceIsExactlyOneMinute(int milliseconds, bool valid)
    {
        var now = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
        var issued = now.AddMilliseconds(milliseconds);
        PlayerCreationOperation.TryGetDeadline(Guid.CreateVersion7(issued), now, out var deadline).ShouldBe(valid);
        deadline.ShouldBe(valid ? issued.AddHours(24) : default);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("01994916-0000-4000-8000-000000000000")]
    [InlineData("ffffffff-ffff-7000-8000-000000000000")]
    [InlineData("01994916-0000-7000-0000-000000000000")]
    public void MalformedOrUnrepresentableOperationReturnsSafeFailure(string identity)
    {
        var operation = Guid.Parse(identity);
        PlayerCreationOperation.TryGetCreatedAt(operation, out var issued).ShouldBeFalse();
        issued.ShouldBe(default);
        PlayerCreationOperation.TryGetDeadline(operation, DateTimeOffset.UtcNow, out var deadline).ShouldBeFalse();
        deadline.ShouldBe(default);
    }
}
