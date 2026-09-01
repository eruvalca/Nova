using Nova.Data.Tenancy;
using Nova.Features.Dashboard;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Dashboard;

/// <summary>Verifies authorization and independent composition of administrator attention projections.</summary>
public sealed class DashboardAttentionQueryServiceTests
{
    /// <summary>Proves one unavailable projection does not erase the other projection's result.</summary>
    [Fact]
    public async Task GetAsync_ComposesProjectionStatesIndependently()
    {
        var reader = new FakeProjectionReader
        {
            Pending = new PendingJoinRequestAttentionDto { State = AttentionProjectionState.Unavailable },
            NeedsPlacement = new NeedsPlacementAttentionDto { State = AttentionProjectionState.Available, Count = 7 }
        };
        var currentUser = Substitute.For<ICurrentUserProvider>();
        currentUser.UserId.Returns(10);
        currentUser.ClubId.Returns(42);
        currentUser.IsClubAdmin.Returns(true);
        var service = new DashboardAttentionQueryService(reader, currentUser);

        var result = await service.GetAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PendingJoinRequests.State.ShouldBe(AttentionProjectionState.Unavailable);
        result.Value.NeedsPlacement.State.ShouldBe(AttentionProjectionState.Available);
        result.Value.NeedsPlacement.Count.ShouldBe(7);
    }

    /// <summary>Proves non-administrators are rejected before either projection runs.</summary>
    [Fact]
    public async Task GetAsync_ReturnsForbidden_BeforeReadingProjections()
    {
        var reader = new FakeProjectionReader();
        var currentUser = Substitute.For<ICurrentUserProvider>();
        currentUser.UserId.Returns(10);
        currentUser.ClubId.Returns(42);
        currentUser.IsClubAdmin.Returns(false);
        var service = new DashboardAttentionQueryService(reader, currentUser);

        var result = await service.GetAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        reader.PendingReads.ShouldBe(0);
        reader.PlacementReads.ShouldBe(0);
    }

    private sealed class FakeProjectionReader : IDashboardAttentionProjectionReader
    {
        public PendingJoinRequestAttentionDto Pending { get; init; } = new()
        {
            State = AttentionProjectionState.Available,
            Count = 0
        };

        public NeedsPlacementAttentionDto NeedsPlacement { get; init; } = new()
        {
            State = AttentionProjectionState.Available,
            Count = 0
        };

        public int PendingReads { get; private set; }

        public int PlacementReads { get; private set; }

        public Task<PendingJoinRequestAttentionDto> ReadPendingAsync(long clubId, CancellationToken cancellationToken)
        {
            PendingReads++;
            return Task.FromResult(Pending);
        }

        public Task<NeedsPlacementAttentionDto> ReadNeedsPlacementAsync(long clubId, CancellationToken cancellationToken)
        {
            PlacementReads++;
            return Task.FromResult(NeedsPlacement);
        }
    }
}
