using Nova.Features.ClubActivity;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Unit.Tests.ActivityPolicies;

public sealed class ClubActivityEventPolicyTests
{
    [Theory]
    [InlineData(PlacementOutcome.Undecided, null, PlacementOutcome.Undecided, null, null)]
    [InlineData(PlacementOutcome.Undecided, null, PlacementOutcome.Assigned, 7L, ClubActivityEventKind.PlacementAssigned)]
    [InlineData(PlacementOutcome.Assigned, 7L, PlacementOutcome.Assigned, 8L, ClubActivityEventKind.PlacementReassigned)]
    [InlineData(PlacementOutcome.Assigned, 7L, PlacementOutcome.Withdrawn, null, ClubActivityEventKind.PlacementOutcomeChanged)]
    public void ClassifyPlacement_ReturnsExpectedTransition(
        PlacementOutcome previousOutcome,
        long? previousTeamId,
        PlacementOutcome currentOutcome,
        long? currentTeamId,
        ClubActivityEventKind? expected)
    {
        var result = ClubActivityEventPolicy.ClassifyPlacement(
            new PlacementActivityState(previousOutcome, previousTeamId, null, null),
            new PlacementActivityState(currentOutcome, currentTeamId, null, null));

        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData(ClubActivityEventKind.CampaignDraftCreated, ClubActivityAudience.Administrators)]
    [InlineData(ClubActivityEventKind.JoinRequestSubmitted, ClubActivityAudience.Administrators)]
    [InlineData(ClubActivityEventKind.CampaignOpened, ClubActivityAudience.AllMembers)]
    [InlineData(ClubActivityEventKind.MemberJoined, ClubActivityAudience.AllMembers)]
    public void AudienceFor_EnforcesRoleVisibility(ClubActivityEventKind kind, ClubActivityAudience expected)
        => ClubActivityEventPolicy.AudienceFor(kind).ShouldBe(expected);
}
