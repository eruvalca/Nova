using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;


public sealed partial class CampaignParticipantQueryServiceTests : IDisposable
{
    private const long ClubAId = 1000;
    private const long ClubBId = 2000;
    private const long ClubAMemberId = 1001;
    private const long ClubBMemberId = 2001;

    private readonly TenancyTestHarness _harness = new();
    private long _campaignAId;
    private long _campaignBId;
    private long _assignmentAId;
    private long _tagAId;
    private long _teamAId;

    public CampaignParticipantQueryServiceTests()
    {
        Seed();
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetParticipantRosterReturnsForbiddenWhenNotMemberAsync()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantRosterAsync(new GetCampaignParticipantRosterInput { CampaignId = _campaignAId }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetParticipantRosterReturnsValidationWhenFilterContainsNonPositiveValuesAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantRosterAsync(new GetCampaignParticipantRosterInput
        {
            CampaignId = _campaignAId,
            GraduationYears = [0],
            TagDefinitionIds = [0]
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    [Fact]
#pragma warning disable MA0051 // Keep the complete arrangement, operation, and assertions together as one regression scenario.
    public async Task GetParticipantRosterFiltersAndPagesWithinTenantAsync()
#pragma warning restore MA0051
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        using (var admin = _harness.CreateAdminContext())
        {
            var player = new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                FirstName = "Avery",
                LastName = "Barnes",
                DateOfBirth = new DateOnly(2010, 1, 1),
                GraduationYear = 2028,
                LifecycleStatus = LifecycleStatus.Active,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            };
            admin.Players.Add(player);
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);

            var assignment = new PlayerCampaignAssignmentEntity
            {
                PlayerId = player.PlayerId,
                CampaignId = _campaignAId,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId,
                PlacementOutcome = PlacementOutcome.Assigned,
                TeamId = _teamAId,
                TryoutNumber = 9
            };
            admin.PlayerCampaignAssignments.Add(assignment);
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);

            admin.CampaignTagApplications.Add(new CampaignTagApplicationEntity
            {
                AuthorDisplayName = "A Member",
                CreationOperationId = Guid.NewGuid(),
                PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId,
                PlayerTagId = _tagAId,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            });
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var pageOne = await service.GetParticipantRosterAsync(
            new GetCampaignParticipantRosterInput
            {
                CampaignId = _campaignAId,
                Search = "avery",
                GraduationYears = [2028, 2029],
                TagDefinitionIds = [_tagAId],
                Outcome = "assigned",
                TeamId = _teamAId,
                Page = 1,
                PageSize = 1
            },
            TestContext.Current.CancellationToken);

        var pageTwo = await service.GetParticipantRosterAsync(
            new GetCampaignParticipantRosterInput
            {
                CampaignId = _campaignAId,
                Search = "avery",
                GraduationYears = [2028, 2029],
                TagDefinitionIds = [_tagAId],
                Outcome = "assigned",
                TeamId = _teamAId,
                Page = 2,
                PageSize = 1
            },
            TestContext.Current.CancellationToken);

        pageOne.IsSuccess.ShouldBeTrue();
        pageOne.Value.TotalCount.ShouldBe(2);
        pageOne.Value.Page.ShouldBe(1);
        pageOne.Value.PageSize.ShouldBe(1);
        pageOne.Value.Items.Count.ShouldBe(1);
        pageOne.Value.Items[0].DisplayName.ShouldBe("Avery Adams");
        pageOne.Value.Items[0].Team.ShouldNotBeNull();
        pageOne.Value.Items[0].AppliedTags.ShouldContain(tag => tag.PlayerTagId == _tagAId);

        pageTwo.IsSuccess.ShouldBeTrue();
        pageTwo.Value.TotalCount.ShouldBe(2);
        pageTwo.Value.Page.ShouldBe(2);
        pageTwo.Value.PageSize.ShouldBe(1);
        pageTwo.Value.Items.Count.ShouldBe(1);
        pageTwo.Value.Items[0].DisplayName.ShouldBe("Avery Barnes");
    }

    [Fact]
    public async Task GetParticipantRosterTreatsSearchWildcardsAsLiteralsAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantRosterAsync(
            new GetCampaignParticipantRosterInput { CampaignId = _campaignAId, Search = "%" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(0);
        result.Value.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetParticipantRosterReturnsNotFoundForCrossTenantCampaignAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantRosterAsync(
            new GetCampaignParticipantRosterInput { CampaignId = _campaignBId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task GetParticipantDetailReturnsNotesAndTagsForAssignmentAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantDetailAsync(
            new GetCampaignParticipantDetailInput { CampaignId = _campaignAId, PlayerCampaignAssignmentId = _assignmentAId },
            TestContext.Current.CancellationToken);

        var evidence = new CampaignEvaluationQueryService(new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()), _harness.CurrentUser);
        var historyInput = new GetEvaluationHistoryInput { CampaignId = _campaignAId, PlayerCampaignAssignmentId = _assignmentAId };
        var notes = await evidence.GetNotesAsync(historyInput, TestContext.Current.CancellationToken);
        var applications = await evidence.GetApplicationsAsync(historyInput, TestContext.Current.CancellationToken);
        notes.IsSuccess.ShouldBeTrue();
        applications.IsSuccess.ShouldBeTrue();
        result.IsSuccess.ShouldBeTrue();
        result.Value.DisplayName.ShouldBe("Avery Adams");
        result.Value.CampaignStatus.ShouldBe(CampaignStatus.Active);
        result.Value.ConcurrencyToken.ShouldNotBe(Guid.Empty);
        notes.Value.Items.ShouldContain(note => note.Content == "Seed note");
        applications.Value.Items.ShouldContain(tag => tag.CampaignTagApplicationId > 0 && tag.TagName == "Blue Tag" && tag.ActorDisplayName == "A Member");
        notes.Value.Items[0].CanEdit.ShouldBeTrue();
        notes.Value.Items[0].CanDelete.ShouldBeTrue();
        applications.Value.Items[0].CanRemove.ShouldBeTrue();
        result.Value.Capabilities.CanAddNote.ShouldBeTrue();
        result.Value.Capabilities.CanApplyTag.ShouldBeTrue();
        result.Value.Capabilities.CanEditPlacement.ShouldBeTrue();
        result.Value.Capabilities.CanArchiveTagDefinitions.ShouldBeFalse();
    }

    [Fact]
    public async Task GetParticipantDetailOrdersNotesAndTagsByDescendingIdWhenTimestampsAreEqualAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        using (var admin = _harness.CreateAdminContext())
        {
            var sameInstant = DateTimeOffset.UtcNow;
            admin.Notes.AddRange(
                new NoteEntity { AuthorDisplayName = "A Member", CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = _assignmentAId, ClubId = ClubAId, Content = "First note", CreatedById = ClubAMemberId, CreatedAt = sameInstant },
                new NoteEntity { AuthorDisplayName = "A Member", CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = _assignmentAId, ClubId = ClubAId, Content = "Second note", CreatedById = ClubAMemberId, CreatedAt = sameInstant });
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);

            var otherTag = (await admin.PlayerTags.SingleAsync(tag => tag.ClubId == ClubAId && tag.Name == "Other Tag", TestContext.Current.CancellationToken));
            var thirdTag = new PlayerTagEntity { CreationOperationId = Guid.NewGuid(), Name = "Third Tag", NormalizedName = "THIRD TAG", Color = "Green", ClubId = ClubAId, CreatedById = ClubAMemberId, LifecycleStatus = LifecycleStatus.Active };
            admin.PlayerTags.Add(thirdTag);
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);

            admin.CampaignTagApplications.AddRange(
                new CampaignTagApplicationEntity { AuthorDisplayName = "A Member", CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = _assignmentAId, PlayerTagId = otherTag.PlayerTagId, ClubId = ClubAId, CreatedById = ClubAMemberId, CreatedAt = sameInstant },
                new CampaignTagApplicationEntity { AuthorDisplayName = "A Member", CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = _assignmentAId, PlayerTagId = thirdTag.PlayerTagId, ClubId = ClubAId, CreatedById = ClubAMemberId, CreatedAt = sameInstant });
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantDetailAsync(
            new GetCampaignParticipantDetailInput { CampaignId = _campaignAId, PlayerCampaignAssignmentId = _assignmentAId },
            TestContext.Current.CancellationToken);

        var evidence = new CampaignEvaluationQueryService(new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()), _harness.CurrentUser);
        var historyInput = new GetEvaluationHistoryInput { CampaignId = _campaignAId, PlayerCampaignAssignmentId = _assignmentAId };
        var notes = await evidence.GetNotesAsync(historyInput, TestContext.Current.CancellationToken);
        var applications = await evidence.GetApplicationsAsync(historyInput, TestContext.Current.CancellationToken);
        notes.IsSuccess.ShouldBeTrue();
        applications.IsSuccess.ShouldBeTrue();
        result.IsSuccess.ShouldBeTrue();
        var pair = notes.Value.Items.Zip(notes.Value.Items.Skip(1));
        pair.All(adjacent => adjacent.First.CreatedAt > adjacent.Second.CreatedAt
                             || (adjacent.First.CreatedAt == adjacent.Second.CreatedAt && adjacent.First.NoteId > adjacent.Second.NoteId))
            .ShouldBeTrue();
        var tagPair = applications.Value.Items.Zip(applications.Value.Items.Skip(1));
        tagPair.All(adjacent => adjacent.First.AppliedAt > adjacent.Second.AppliedAt
                                || (adjacent.First.AppliedAt == adjacent.Second.AppliedAt && adjacent.First.CampaignTagApplicationId > adjacent.Second.CampaignTagApplicationId))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task GetParticipantDetailDoesNotExposePlacementEditWhenPlayerArchivedAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        using (var admin = _harness.CreateAdminContext())
        {
            var player = (await admin.Players.SingleAsync(candidate => candidate.ClubId == ClubAId && candidate.FirstName == "Avery", TestContext.Current.CancellationToken));
            player.LifecycleStatus = LifecycleStatus.Archived;
            player.ArchivedAt = DateTimeOffset.UtcNow;
            player.ArchivedById = ClubAMemberId;
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantDetailAsync(
            new GetCampaignParticipantDetailInput { CampaignId = _campaignAId, PlayerCampaignAssignmentId = _assignmentAId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Capabilities.CanEditPlacement.ShouldBeFalse();
    }

    [Fact]
    public async Task GetParticipantDetailDoesNotExposeTagRemovalWhenTagDefinitionArchivedAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        using (var admin = _harness.CreateAdminContext())
        {
            var tag = (await admin.PlayerTags.SingleAsync(candidate => candidate.ClubId == ClubAId && candidate.Name == "Blue Tag", TestContext.Current.CancellationToken));
            tag.LifecycleStatus = LifecycleStatus.Archived;
            tag.ArchivedAt = DateTimeOffset.UtcNow;
            tag.ArchivedById = ClubAMemberId;
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantDetailAsync(
            new GetCampaignParticipantDetailInput { CampaignId = _campaignAId, PlayerCampaignAssignmentId = _assignmentAId },
            TestContext.Current.CancellationToken);

        var evidence = new CampaignEvaluationQueryService(new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()), _harness.CurrentUser);
        var historyInput = new GetEvaluationHistoryInput { CampaignId = _campaignAId, PlayerCampaignAssignmentId = _assignmentAId };
        var notes = await evidence.GetNotesAsync(historyInput, TestContext.Current.CancellationToken);
        var applications = await evidence.GetApplicationsAsync(historyInput, TestContext.Current.CancellationToken);
        notes.IsSuccess.ShouldBeTrue();
        applications.IsSuccess.ShouldBeTrue();
        result.IsSuccess.ShouldBeTrue();
        applications.Value.Items.Single(tag => string.Equals(tag.TagName, "Blue Tag", StringComparison.Ordinal)).CanRemove.ShouldBeFalse();
    }

    public static TheoryData<string, string, string[]> SortDirectionCases => new()
    {
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "displayName", "asc", new[] { "Avery Adams", "Brett Baker" } },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "displayName", "desc", new[] { "Brett Baker", "Avery Adams" } },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "assignmentId", "asc", new[] { "Avery Adams", "Brett Baker" } },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "assignmentId", "desc", new[] { "Brett Baker", "Avery Adams" } },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "graduationYear", "asc", new[] { "Avery Adams", "Brett Baker" } },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "graduationYear", "desc", new[] { "Brett Baker", "Avery Adams" } },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "tryoutNumber", "asc", new[] { "Avery Adams", "Brett Baker" } },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "tryoutNumber", "desc", new[] { "Brett Baker", "Avery Adams" } },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "outcome", "asc", new[] { "Brett Baker", "Avery Adams" } },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "outcome", "desc", new[] { "Avery Adams", "Brett Baker" } },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "teamName", "asc", new[] { "Brett Baker", "Avery Adams" } },
#pragma warning restore CA1861
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
        { "teamName", "desc", new[] { "Avery Adams", "Brett Baker" } },
#pragma warning restore CA1861
    };

    /// <summary>
    /// Verifies every documented sort key honors the requested direction over the seeded participants.
    /// </summary>
    [Theory(IncludeTestCaseIndex = true)]
    [MemberData(nameof(SortDirectionCases))]
    public async Task GetParticipantRosterAppliesSortKeyAndDirectionAsync(string sortBy, string sortDirection, string[] expectedDisplayNames)
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantRosterAsync(
            new GetCampaignParticipantRosterInput
            {
                CampaignId = _campaignAId,
                SortBy = sortBy,
                SortDirection = sortDirection,
                PageSize = GetCampaignParticipantRosterInput.MaxPageSize
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(item => item.DisplayName).ShouldBe(expectedDisplayNames);
    }

    /// <summary>
    /// Verifies equal sort keys are ordered by ascending assignment identifier in both directions.
    /// </summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("displayName")]
    [InlineData("graduationYear")]
    [InlineData("tryoutNumber")]
    [InlineData("outcome")]
    [InlineData("teamName")]
    public async Task GetParticipantRosterAppliesAscendingAssignmentIdTieBreakerAsync(string sortBy)
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        long firstAssignmentId;
        long secondAssignmentId;
        using (var admin = _harness.CreateAdminContext())
        {
            var first = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Dana", LastName = "Davis", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId };
            var second = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Dana", LastName = "Davis", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId };
            admin.Players.AddRange(first, second);
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);

            var firstAssignment = new PlayerCampaignAssignmentEntity { PlayerId = first.PlayerId, CampaignId = _campaignAId, ClubId = ClubAId, CreatedById = ClubAMemberId, PlacementOutcome = PlacementOutcome.Assigned, TeamId = _teamAId };
            var secondAssignment = new PlayerCampaignAssignmentEntity { PlayerId = second.PlayerId, CampaignId = _campaignAId, ClubId = ClubAId, CreatedById = ClubAMemberId, PlacementOutcome = PlacementOutcome.Assigned, TeamId = _teamAId };
            admin.PlayerCampaignAssignments.AddRange(firstAssignment, secondAssignment);
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);

            firstAssignmentId = firstAssignment.PlayerCampaignAssignmentId;
            secondAssignmentId = secondAssignment.PlayerCampaignAssignmentId;
        }

        foreach (var direction in new[] { "asc", "desc" })
        {
            var service = new CampaignParticipantQueryService(
                new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
                _harness.CurrentUser,
                NullLogger<CampaignParticipantQueryService>.Instance);

            var result = await service.GetParticipantRosterAsync(
                new GetCampaignParticipantRosterInput
                {
                    CampaignId = _campaignAId,
                    SortBy = sortBy,
                    SortDirection = direction,
                    PageSize = GetCampaignParticipantRosterInput.MaxPageSize
                },
                TestContext.Current.CancellationToken);

            result.IsSuccess.ShouldBeTrue();
            var ids = result.Value.Items.Select(item => item.PlayerCampaignAssignmentId).ToArray();
            ids.ShouldContain(firstAssignmentId);
            ids.ShouldContain(secondAssignmentId);
            Array.IndexOf(ids, firstAssignmentId).ShouldBeLessThan(Array.IndexOf(ids, secondAssignmentId));
        }
    }

    /// <summary>
    /// Verifies the graduation-years query returns the campaign's distinct years in ascending order.
    /// </summary>
    [Fact]
    public async Task GetRosterGraduationYearsReturnsDistinctAscendingYearsForClubCampaignAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = _campaignAId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe([2028, 2029]);
    }

    /// <summary>
    /// Verifies the graduation-years query returns an empty list for a campaign without participants.
    /// </summary>
    [Fact]
    public async Task GetRosterGraduationYearsReturnsEmptyListWhenCampaignHasNoParticipantsAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        long emptyCampaignId;
        using (var admin = _harness.CreateAdminContext())
        {
            (await admin.Campaigns.SingleAsync(campaign => campaign.ClubId == ClubAId
                && campaign.Status == CampaignStatus.Active, TestContext.Current.CancellationToken)).Status = CampaignStatus.Draft;
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);
            var season = (await admin.Seasons.SingleAsync(season => season.ClubId == ClubAId, TestContext.Current.CancellationToken));
            var campaign = new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = "Empty Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                Status = CampaignStatus.Active,
                SeasonId = season.SeasonId,
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            };
            admin.Campaigns.Add(campaign);
            await admin.SaveChangesAsync(TestContext.Current.CancellationToken);
            emptyCampaignId = campaign.CampaignId;
        }

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = emptyCampaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies the graduation-years query is rejected for a caller without a club scope.
    /// </summary>
    [Fact]
    public async Task GetRosterGraduationYearsReturnsForbiddenWhenUserHasNoClubAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = null;

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = _campaignAId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies the graduation-years query is rejected for an anonymous caller.
    /// </summary>
    [Fact]
    public async Task GetRosterGraduationYearsReturnsForbiddenWhenNotSignedInAsync()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = _campaignAId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies a campaign owned by another club is treated as not found.
    /// </summary>
    [Fact]
    public async Task GetRosterGraduationYearsReturnsNotFoundForCrossTenantCampaignAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = _campaignBId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies a non-positive campaign identifier is rejected before querying.
    /// </summary>
    [Fact]
    public async Task GetRosterGraduationYearsReturnsValidationForNonPositiveCampaignIdAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignParticipantQueryService(
            new Nova.Unit.Tests.Account.TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

#pragma warning disable MA0051 // Keep the complete arrangement, operation, and assertions together as one regression scenario.
    private void Seed()
#pragma warning restore MA0051
    {
        using var admin = _harness.CreateAdminContext();

        admin.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "A", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "B", State = "MA", CreatedById = ClubBMemberId });

        admin.Users.AddRange(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "A", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "B", LastName = "Member", ClubId = ClubBId });

        var season = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "Season 1", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubAId, CreatedById = ClubAMemberId };
        var seasonB = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "Season 2", StartDate = new DateOnly(2026, 2, 1), ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Seasons.AddRange(season, seasonB);

        admin.SaveChanges();

        var campaignA = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Campaign A", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = season.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignB = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Campaign B", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = seasonB.SeasonId, ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Campaigns.AddRange(campaignA, campaignB);

        admin.Teams.AddRange(
            new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "Alpha Team", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "Beta Team", GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubBId, CreatedById = ClubBMemberId });

        admin.Players.AddRange(
            new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Avery", LastName = "Adams", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Brett", LastName = "Baker", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Cora", LastName = "Clark", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubBId, CreatedById = ClubBMemberId });

        admin.PlayerTags.AddRange(
            new PlayerTagEntity { CreationOperationId = Guid.NewGuid(), Name = "Blue Tag", NormalizedName = "BLUE TAG", Color = "Blue", ClubId = ClubAId, CreatedById = ClubAMemberId, LifecycleStatus = LifecycleStatus.Active },
            new PlayerTagEntity { CreationOperationId = Guid.NewGuid(), Name = "Other Tag", NormalizedName = "OTHER TAG", Color = "Red", ClubId = ClubAId, CreatedById = ClubAMemberId, LifecycleStatus = LifecycleStatus.Active });

        admin.SaveChanges();

        _campaignAId = campaignA.CampaignId;
        _campaignBId = campaignB.CampaignId;

        var teamA = admin.Teams.Single(team => team.ClubId == ClubAId && team.Name == "Alpha Team");
        _teamAId = teamA.TeamId;
        var playerA = admin.Players.Single(player => player.ClubId == ClubAId && player.FirstName == "Avery");
        var playerB = admin.Players.Single(player => player.ClubId == ClubAId && player.FirstName == "Brett");
        var clubBPlayer = admin.Players.Single(player => player.ClubId == ClubBId && player.FirstName == "Cora");
        var tagA = admin.PlayerTags.Single(tag => tag.ClubId == ClubAId && tag.Name == "Blue Tag");

        var assignmentA = new PlayerCampaignAssignmentEntity { PlayerId = playerA.PlayerId, CampaignId = campaignA.CampaignId, ClubId = ClubAId, CreatedById = ClubAMemberId, PlacementOutcome = PlacementOutcome.Assigned, TeamId = teamA.TeamId, TryoutNumber = 7 };
        var assignmentB = new PlayerCampaignAssignmentEntity { PlayerId = playerB.PlayerId, CampaignId = campaignA.CampaignId, ClubId = ClubAId, CreatedById = ClubAMemberId, PlacementOutcome = PlacementOutcome.Undecided, TryoutNumber = 8 };
        var assignmentC = new PlayerCampaignAssignmentEntity { PlayerId = clubBPlayer.PlayerId, CampaignId = campaignB.CampaignId, ClubId = ClubBId, CreatedById = ClubBMemberId, PlacementOutcome = PlacementOutcome.Undecided };
        admin.PlayerCampaignAssignments.AddRange(assignmentA, assignmentB, assignmentC);
        admin.SaveChanges();

        _assignmentAId = assignmentA.PlayerCampaignAssignmentId;
        _tagAId = tagA.PlayerTagId;

        admin.CampaignTagApplications.AddRange(
            new CampaignTagApplicationEntity { AuthorDisplayName = "A Member", CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = assignmentA.PlayerCampaignAssignmentId, PlayerTagId = tagA.PlayerTagId, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new CampaignTagApplicationEntity { AuthorDisplayName = "A Member", CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = assignmentB.PlayerCampaignAssignmentId, PlayerTagId = tagA.PlayerTagId, ClubId = ClubAId, CreatedById = ClubAMemberId });

        admin.Notes.AddRange(
            new NoteEntity { AuthorDisplayName = "A Member", CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = assignmentA.PlayerCampaignAssignmentId, ClubId = ClubAId, Content = "Seed note", CreatedById = ClubAMemberId });

        admin.SaveChanges();
    }
}
