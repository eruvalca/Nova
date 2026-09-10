using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class EffectivePlacementQueryServiceTests
{
    [Fact]
    public async Task DiscoveryCombinesYearsAndCampaignTagsWithoutChangingWholeCampaignCountsAsync()
    {
        var first = AddDecision(AddPlayer("Avery", graduationYear: 2028), ActiveCampaignId, PlacementOutcome.NotSelected);
        var second = AddDecision(AddPlayer("Blake", graduationYear: 2029), ActiveCampaignId, PlacementOutcome.NotSelected);
        var excludedYear = AddDecision(AddPlayer("Casey", graduationYear: 2030), ActiveCampaignId, PlacementOutcome.NotSelected);
        AddDecision(AddPlayer("Devon", graduationYear: 2028), ActiveCampaignId, PlacementOutcome.NotSelected);
        var excludedOutcome = AddDecision(AddPlayer("Emery", graduationYear: 2028), ActiveCampaignId, PlacementOutcome.Undecided);
        var tagA = AddTag("Captain", first, excludedYear, excludedOutcome);
        var tagB = AddTag("Returning", second);
        var input = new GetCampaignEffectivePlacementsInput
        {
            CampaignId = ActiveCampaignId,
            GraduationYears = [2028, 2029],
            TagDefinitionIds = [tagA, tagB],
            LocalOutcome = "notselected",
            SortBy = "displayName",
            SortDirection = "desc",
            PageSize = 1,
        };

        var result = await WorkAsync(input);
        result.Counts.ShouldBe(new EffectivePlacementCounts(1, 0, 4, 0));
        result.Participants.TotalCount.ShouldBe(2);
        var row = result.Participants.Items.ShouldHaveSingleItem();
        row.PlayerCampaignAssignmentId.ShouldBe(second.PlayerCampaignAssignmentId);
        row.AppliedTags.ShouldHaveSingleItem().PlayerTagId.ShouldBe(tagB);
        (await WorkAsync(input with { Page = 2 })).Participants.Items.ShouldHaveSingleItem()
            .PlayerCampaignAssignmentId.ShouldBe(first.PlayerCampaignAssignmentId);
        (await WorkAsync(input with { ParticipantId = first.PlayerCampaignAssignmentId })).Participants.Items
            .ShouldHaveSingleItem().AppliedTags.ShouldHaveSingleItem().PlayerTagId.ShouldBe(tagA);
    }

    [Fact]
    public async Task LocalTeamAndOutcomeFiltersDoNotSelectInheritedEffectiveAssignmentsAsync()
    {
        var inheritedPlayer = AddPlayer("Inherited");
        AddDecision(inheritedPlayer, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        var inherited = AddDecision(inheritedPlayer, ActiveCampaignId, PlacementOutcome.Undecided);
        var local = AddDecision(AddPlayer("Local"), ActiveCampaignId, PlacementOutcome.Assigned, TeamId);

        (await WorkAsync(new() { CampaignId = ActiveCampaignId, TeamId = TeamId })).Participants.TotalCount.ShouldBe(2);
        var localResult = await WorkAsync(new() { CampaignId = ActiveCampaignId, LocalTeamId = TeamId, LocalOutcome = "assigned" });
        var row = localResult.Participants.Items.ShouldHaveSingleItem();
        row.PlayerCampaignAssignmentId.ShouldBe(local.PlayerCampaignAssignmentId);
        row.LocalTeam!.TeamName.ShouldBe("Alpha");
        var inheritedResult = await WorkAsync(new() { CampaignId = ActiveCampaignId, LocalOutcome = "undecided", TeamId = TeamId });
        inheritedResult.Participants.Items.ShouldHaveSingleItem().PlayerCampaignAssignmentId.ShouldBe(inherited.PlayerCampaignAssignmentId);
        inheritedResult.Participants.Items.Single().LocalTeam.ShouldBeNull();
        inheritedResult.Counts.OptionalReassignment.ShouldBe(2);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("graduationYear")]
    [InlineData("tryoutNumber")]
    [InlineData("outcome")]
    [InlineData("teamName")]
    [InlineData("displayName")]
    [InlineData("assignmentId")]
    public async Task ExplicitDiscoverySortsKeepDeterministicPagesAsync(string sortBy)
    {
        var first = AddDecision(AddPlayer("Alex", "Same"), ActiveCampaignId, PlacementOutcome.Undecided, tryoutNumber: 70);
        var second = AddDecision(AddPlayer("Alex", "Same"), ActiveCampaignId, PlacementOutcome.Undecided, tryoutNumber: 71);
        var input = new GetCampaignEffectivePlacementsInput { CampaignId = ActiveCampaignId, SortBy = sortBy, SortDirection = "asc", PageSize = 1 };
        (await WorkAsync(input)).Participants.Items.ShouldHaveSingleItem().PlayerCampaignAssignmentId.ShouldBe(first.PlayerCampaignAssignmentId);
        (await WorkAsync(input with { Page = 2 })).Participants.Items.ShouldHaveSingleItem().PlayerCampaignAssignmentId.ShouldBe(second.PlayerCampaignAssignmentId);
        var descending = await WorkAsync(input with { SortDirection = "desc" });
        descending.Participants.Items.ShouldHaveSingleItem().PlayerCampaignAssignmentId
            .ShouldBe(sortBy is "tryoutNumber" or "assignmentId" ? second.PlayerCampaignAssignmentId : first.PlayerCampaignAssignmentId);
    }

    [Fact]
    public async Task ClosedDiscoveryRetainsWholeCountLocalEvidenceAndArchivedTagsAsync()
    {
        var selected = AddDecision(AddPlayer("Avery", archived: true), PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(AddPlayer("Blake"), PriorCampaignId, PlacementOutcome.Withdrawn);
        AddDecision(AddPlayer("Casey"), PriorCampaignId, PlacementOutcome.NotSelected);
        var tagId = AddTag("Historic", selected);
        using (var db = _harness.CreateAdminContext())
        {
            var tag = db.PlayerTags.Single(tag => tag.PlayerTagId == tagId);
            tag.LifecycleStatus = LifecycleStatus.Archived;
            tag.ArchivedAt = DateTimeOffset.UnixEpoch;
            tag.ArchivedById = MemberId;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var result = await CreateService().GetClosedCampaignRosterAsync(new()
        {
            CampaignId = PriorCampaignId,
            GraduationYears = [2028, 2029],
            TagDefinitionIds = [tagId],
            LocalTeamId = TeamId,
            LocalOutcome = "assigned",
            Search = "Avery",
            SortBy = "displayName",
            SortDirection = "desc",
        }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ParticipantCount.ShouldBe(3);
        result.Value.Participants.TotalCount.ShouldBe(1);
        var row = result.Value.Participants.Items.ShouldHaveSingleItem();
        row.Source.Decision.PlayerCampaignAssignmentId.ShouldBe(selected.PlayerCampaignAssignmentId);
        row.AppliedTags.ShouldHaveSingleItem().IsArchived.ShouldBeTrue();
    }

    [Fact]
    public async Task ClosedDiscoveryCannotHideIncompleteRecordEvenWithExactParticipantFilterAsync()
    {
        AddDecision(AddPlayer("Incomplete"), PriorCampaignId, PlacementOutcome.Undecided);
        var complete = AddDecision(AddPlayer("Complete"), PriorCampaignId, PlacementOutcome.NotSelected);
        var result = await CreateService().GetClosedCampaignRosterAsync(new()
        {
            CampaignId = PriorCampaignId,
            ParticipantId = complete.PlayerCampaignAssignmentId,
            LocalOutcome = "notselected",
            Search = "Complete",
        }, TestContext.Current.CancellationToken);
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task DiscoveryRejectsInvisibleIdentifiersAndDoesNotExposeForeignParticipantsAsync()
    {
        var service = CreateService();
        (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = ActiveCampaignId, LocalTeamId = 9999 },
            TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        (await service.GetClosedCampaignRosterAsync(new() { CampaignId = PriorCampaignId, TagDefinitionIds = [9999] },
            TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        (await WorkAsync(new() { CampaignId = ActiveCampaignId, ParticipantId = 9999 })).Participants.Items.ShouldBeEmpty();
    }

    private long AddTag(string name, params PlayerCampaignAssignmentEntity[] assignments)
    {
        using var db = _harness.CreateAdminContext();
        var tag = new PlayerTagEntity
        {
            CreationOperationId = Guid.NewGuid(),
            ClubId = ClubId,
            CreatedById = MemberId,
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Color = "#123456",
        };
        db.PlayerTags.Add(tag);
        db.SaveChanges();
        db.CampaignTagApplications.AddRange(assignments.Select(assignment => new CampaignTagApplicationEntity
        {
            CreationOperationId = Guid.NewGuid(),
            ClubId = ClubId,
            CreatedById = MemberId,
            PlayerTagId = tag.PlayerTagId,
            PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId,
        }));
        db.SaveChanges();
        return tag.PlayerTagId;
    }
}
