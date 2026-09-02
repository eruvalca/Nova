using Nova.Features.Activity;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Shouldly;

namespace Nova.Unit.Tests.Activity;

/// <summary>
/// Verifies the deterministic kind-to-family and kind-to-visibility rules of the activity
/// foundation, along with placement transition classification and context matching.
/// </summary>
public sealed class ActivityEventPolicyTests
{
    /// <summary>Verifies every kind maps to its documented family.</summary>
    [Theory]
    [InlineData(ActivityEventKind.CampaignDraftCreated, "CampaignLifecycle")]
    [InlineData(ActivityEventKind.CampaignDraftDeleted, "CampaignLifecycle")]
    [InlineData(ActivityEventKind.CampaignOpened, "CampaignLifecycle")]
    [InlineData(ActivityEventKind.CampaignClosed, "CampaignLifecycle")]
    [InlineData(ActivityEventKind.CampaignReopened, "CampaignLifecycle")]
    [InlineData(ActivityEventKind.PlacementAssigned, "Placement")]
    [InlineData(ActivityEventKind.PlacementNotSelected, "Placement")]
    [InlineData(ActivityEventKind.PlacementWithdrawn, "Placement")]
    [InlineData(ActivityEventKind.PlacementReassigned, "Placement")]
    [InlineData(ActivityEventKind.PlacementOutcomeReplaced, "Placement")]
    [InlineData(ActivityEventKind.PlacementSuperseded, "Placement")]
    [InlineData(ActivityEventKind.JoinRequestSubmitted, "JoinRequest")]
    [InlineData(ActivityEventKind.JoinRequestCancelled, "JoinRequest")]
    [InlineData(ActivityEventKind.JoinRequestRejected, "JoinRequest")]
    [InlineData(ActivityEventKind.MemberJoined, "Membership")]
    [InlineData(ActivityEventKind.MemberRemoved, "Membership")]
    [InlineData(ActivityEventKind.MemberLeft, "Membership")]
    [InlineData(ActivityEventKind.MemberPromoted, "MemberRole")]
    [InlineData(ActivityEventKind.MemberDemoted, "MemberRole")]
    public void FamilyFor_ReturnsDocumentedFamily(ActivityEventKind kind, string family)
    {
        ActivityEventPolicy.FamilyFor(kind).ToString().ShouldBe(family);
    }

    /// <summary>Verifies draft and unresolved join-request kinds are administrator-only.</summary>
    [Theory]
    [InlineData(ActivityEventKind.CampaignDraftCreated, true)]
    [InlineData(ActivityEventKind.CampaignDraftDeleted, true)]
    [InlineData(ActivityEventKind.CampaignOpened, false)]
    [InlineData(ActivityEventKind.CampaignClosed, false)]
    [InlineData(ActivityEventKind.CampaignReopened, false)]
    [InlineData(ActivityEventKind.PlacementAssigned, false)]
    [InlineData(ActivityEventKind.PlacementNotSelected, false)]
    [InlineData(ActivityEventKind.PlacementWithdrawn, false)]
    [InlineData(ActivityEventKind.PlacementReassigned, false)]
    [InlineData(ActivityEventKind.PlacementOutcomeReplaced, false)]
    [InlineData(ActivityEventKind.PlacementSuperseded, false)]
    [InlineData(ActivityEventKind.JoinRequestSubmitted, true)]
    [InlineData(ActivityEventKind.JoinRequestCancelled, true)]
    [InlineData(ActivityEventKind.JoinRequestRejected, true)]
    [InlineData(ActivityEventKind.MemberJoined, false)]
    [InlineData(ActivityEventKind.MemberRemoved, false)]
    [InlineData(ActivityEventKind.MemberLeft, false)]
    [InlineData(ActivityEventKind.MemberPromoted, false)]
    [InlineData(ActivityEventKind.MemberDemoted, false)]
    public void IsAdminOnly_ReturnsDocumentedVisibility(ActivityEventKind kind, bool expected)
    {
        ActivityEventPolicy.IsAdminOnly(kind).ShouldBe(expected);
    }

    /// <summary>Verifies an unchanged save emits no placement event.</summary>
    [Fact]
    public void ClassifyPlacementTransition_ReturnsNull_WhenNothingChanged()
    {
        var result = ActivityEventPolicy.ClassifyPlacementTransition(
            PlacementOutcome.Assigned, 5, PlacementOutcome.Assigned, 5);

        result.ShouldBeNull();
    }

    /// <summary>Verifies a team change within the same campaign is a reassignment.</summary>
    [Fact]
    public void ClassifyPlacementTransition_ReturnsReassigned_WhenTeamChangesInsideSameCampaign()
    {
        var result = ActivityEventPolicy.ClassifyPlacementTransition(
            PlacementOutcome.Assigned, 5, PlacementOutcome.Assigned, 6);

        result.ShouldBe(ActivityEventKind.PlacementReassigned);
    }

    /// <summary>Verifies removing a team from an assigned placement emits no event.</summary>
    [Fact]
    public void ClassifyPlacementTransition_ReturnsNull_WhenTeamIsRemovedFromAssigned()
    {
        var result = ActivityEventPolicy.ClassifyPlacementTransition(
            PlacementOutcome.Assigned, 5, PlacementOutcome.Assigned, null);

        result.ShouldBeNull();
    }

    /// <summary>Verifies an undecided-to-assigned transition is a fresh assignment.</summary>
    [Fact]
    public void ClassifyPlacementTransition_ReturnsAssigned_WhenUndecidedBecomesAssigned()
    {
        var result = ActivityEventPolicy.ClassifyPlacementTransition(
            PlacementOutcome.Undecided, null, PlacementOutcome.Assigned, 5);

        result.ShouldBe(ActivityEventKind.PlacementAssigned);
    }

    /// <summary>Verifies a previously unknown outcome becoming assigned is treated as an assignment.</summary>
    [Fact]
    public void ClassifyPlacementTransition_ReturnsAssigned_WhenPreviousOutcomeWasUndefined()
    {
        // (PlacementOutcome)99 is not defined; the policy treats it like a first assignment.
        var result = ActivityEventPolicy.ClassifyPlacementTransition(
            (PlacementOutcome)99, null, PlacementOutcome.Assigned, 5);

        result.ShouldBe(ActivityEventKind.PlacementAssigned);
    }

    /// <summary>Verifies an assignment outcome change (for example, Withdrawn reverted to Assigned)
    /// is an outcome replacement.</summary>
    [Fact]
    public void ClassifyPlacementTransition_ReturnsOutcomeReplaced_WhenOutcomeChangesAndNoTeamChanges()
    {
        var result = ActivityEventPolicy.ClassifyPlacementTransition(
            PlacementOutcome.Withdrawn, null, PlacementOutcome.Assigned, null);

        result.ShouldBe(ActivityEventKind.PlacementOutcomeReplaced);
    }

    /// <summary>Verifies an outcome change accompanied by a team change stays an outcome replacement.</summary>
    [Fact]
    public void ClassifyPlacementTransition_ReturnsOutcomeReplaced_WhenTeamAlsoChanges()
    {
        var result = ActivityEventPolicy.ClassifyPlacementTransition(
            PlacementOutcome.Withdrawn, null, PlacementOutcome.Assigned, 5);

        result.ShouldBe(ActivityEventKind.PlacementOutcomeReplaced);
    }

    /// <summary>Verifies a move to NotSelected yields the dedicated kind.</summary>
    [Fact]
    public void ClassifyPlacementTransition_ReturnsNotSelected_WhenOutcomeBecomesNotSelected()
    {
        var result = ActivityEventPolicy.ClassifyPlacementTransition(
            PlacementOutcome.Undecided, null, PlacementOutcome.NotSelected, null);

        result.ShouldBe(ActivityEventKind.PlacementNotSelected);
    }

    /// <summary>Verifies a move to Withdrawn yields the dedicated kind.</summary>
    [Fact]
    public void ClassifyPlacementTransition_ReturnsWithdrawn_WhenOutcomeBecomesWithdrawn()
    {
        var result = ActivityEventPolicy.ClassifyPlacementTransition(
            PlacementOutcome.Assigned, 5, PlacementOutcome.Withdrawn, null);

        result.ShouldBe(ActivityEventKind.PlacementWithdrawn);
    }

    /// <summary>Verifies an unknown outcome change is recorded as a replaced outcome.</summary>
    [Fact]
    public void ClassifyPlacementTransition_ReturnsOutcomeReplaced_ForUnknownOutcome()
    {
        var result = ActivityEventPolicy.ClassifyPlacementTransition(
            PlacementOutcome.Undecided, null, (PlacementOutcome)99, null);

        result.ShouldBe(ActivityEventKind.PlacementOutcomeReplaced);
    }

    /// <summary>Verifies a matching context family is accepted.</summary>
    [Theory]
    [InlineData(ActivityEventKind.CampaignClosed)]
    [InlineData(ActivityEventKind.PlacementAssigned)]
    [InlineData(ActivityEventKind.JoinRequestRejected)]
    [InlineData(ActivityEventKind.MemberJoined)]
    [InlineData(ActivityEventKind.MemberPromoted)]
    public void ContextMatchesKind_ReturnsTrue_WhenFamiliesMatch(ActivityEventKind kind)
    {
        var context = kind switch
        {
            ActivityEventKind.PlacementAssigned => (ClubActivityContext)new PlacementContext
            {
                CampaignId = 1,
                CampaignName = "C",
                PlayerCampaignAssignmentId = 1,
                PlayerDisplayName = "P",
                Outcome = PlacementOutcome.Assigned,
            },
            ActivityEventKind.JoinRequestRejected => new JoinRequestContext
            {
                JoinRequestId = 1,
                RequesterDisplayName = "R",
            },
            ActivityEventKind.MemberJoined => new MembershipContext
            {
                MemberUserId = 99,
                MemberDisplayName = "M",
            },
            ActivityEventKind.MemberPromoted => new MemberRoleContext
            {
                MemberDisplayName = "M",
                Role = "ClubAdmin",
            },
            _ => new CampaignLifecycleContext
            {
                CampaignId = 1,
                CampaignName = "C",
            },
        };

        ActivityEventPolicy.ContextMatchesKind(kind, context).ShouldBeTrue();
    }

    /// <summary>Verifies a context from a different family is rejected.</summary>
    [Fact]
    public void ContextMatchesKind_ReturnsFalse_WhenFamiliesDiffer()
    {
        var context = new JoinRequestContext
        {
            JoinRequestId = 1,
            RequesterDisplayName = "R",
        };

        ActivityEventPolicy.ContextMatchesKind(ActivityEventKind.MemberJoined, context).ShouldBeFalse();
    }
}
