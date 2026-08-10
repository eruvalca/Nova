using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

file sealed class CampaignParticipantReadHarnessDbContextFactory(TenancyTestHarness harness) : IDbContextFactory<NovaReadDbContext>
{
    public NovaReadDbContext CreateDbContext() => harness.CreateReadContext();

    public Task<NovaReadDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(harness.CreateReadContext());
}

public sealed class CampaignParticipantQueryServiceTests : IDisposable
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
    public async Task GetParticipantRoster_ReturnsForbidden_WhenNotMember()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var service = new CampaignParticipantQueryService(
            new CampaignParticipantReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantRosterAsync(new GetCampaignParticipantRosterInput { CampaignId = _campaignAId }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetParticipantRoster_FiltersAndPagesWithinTenant()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignParticipantQueryService(
            new CampaignParticipantReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantRosterAsync(
            new GetCampaignParticipantRosterInput
            {
                CampaignId = _campaignAId,
                Search = "avery",
                GraduationYears = [2028],
                TagDefinitionIds = [_tagAId],
                Outcome = "assigned",
                TeamId = _teamAId,
                Page = 1,
                PageSize = 50
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items.Count.ShouldBe(1);
        result.Value.Items[0].DisplayName.ShouldBe("Avery Adams");
        result.Value.Items[0].Team.ShouldNotBeNull();
        result.Value.Items[0].AppliedTags.ShouldContain(tag => tag.PlayerTagId == _tagAId);
    }

    [Fact]
    public async Task GetParticipantDetail_ReturnsNotesAndTagsForAssignment()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignParticipantQueryService(
            new CampaignParticipantReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var result = await service.GetParticipantDetailAsync(
            new GetCampaignParticipantDetailInput { CampaignId = _campaignAId, PlayerCampaignAssignmentId = _assignmentAId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DisplayName.ShouldBe("Avery Adams");
        result.Value.Notes.ShouldContain(note => note.Content == "Seed note");
        result.Value.AppliedTags.ShouldContain(tag => tag.TagName == "Blue Tag");
        result.Value.Capabilities.CanEditNotes.ShouldBeTrue();
    }

    private void Seed()
    {
        using var admin = _harness.CreateAdminContext();

        admin.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "A", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "B", State = "MA", CreatedById = ClubBMemberId });

        admin.Users.AddRange(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "A", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "B", LastName = "Member", ClubId = ClubBId });

        var season = new SeasonEntity { Name = "Season 1", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubAId, CreatedById = ClubAMemberId };
        var seasonB = new SeasonEntity { Name = "Season 2", StartDate = new DateOnly(2026, 2, 1), ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Seasons.AddRange(season, seasonB);

        admin.SaveChanges();

        var campaignA = new CampaignEntity { Name = "Campaign A", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = season.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignB = new CampaignEntity { Name = "Campaign B", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = seasonB.SeasonId, ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Campaigns.AddRange(campaignA, campaignB);

        admin.Teams.AddRange(
            new TeamEntity { Name = "Alpha Team", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new TeamEntity { Name = "Beta Team", GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubBId, CreatedById = ClubBMemberId });

        admin.Players.AddRange(
            new PlayerEntity { FirstName = "Avery", LastName = "Adams", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new PlayerEntity { FirstName = "Brett", LastName = "Baker", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new PlayerEntity { FirstName = "Cora", LastName = "Clark", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubBId, CreatedById = ClubBMemberId });

        admin.PlayerTags.AddRange(
            new PlayerTagEntity { Name = "Blue Tag", Color = "Blue", ClubId = ClubAId, CreatedById = ClubAMemberId, LifecycleStatus = LifecycleStatus.Active },
            new PlayerTagEntity { Name = "Other Tag", Color = "Red", ClubId = ClubAId, CreatedById = ClubAMemberId, LifecycleStatus = LifecycleStatus.Active });

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
            new CampaignTagApplicationEntity { PlayerCampaignAssignmentId = assignmentA.PlayerCampaignAssignmentId, PlayerTagId = tagA.PlayerTagId, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new CampaignTagApplicationEntity { PlayerCampaignAssignmentId = assignmentB.PlayerCampaignAssignmentId, PlayerTagId = tagA.PlayerTagId, ClubId = ClubAId, CreatedById = ClubAMemberId });

        admin.Notes.AddRange(
            new NoteEntity { PlayerCampaignAssignmentId = assignmentA.PlayerCampaignAssignmentId, ClubId = ClubAId, Content = "Seed note", CreatedById = ClubAMemberId });

        admin.SaveChanges();
    }
}
