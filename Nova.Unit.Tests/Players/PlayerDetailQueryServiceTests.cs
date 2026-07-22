using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests for <see cref="PlayerDetailQueryService"/> query ordering, archived-history retention,
/// missing-actor fallbacks, authorization, and tenant isolation.
/// </summary>
public sealed class PlayerDetailQueryServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAViewerId = 200;
    private const long ClubAOtherUserId = 201;
    private const long ClubBViewerId = 300;
    private const long MissingActorUserId = 999_999;

    private const long ClubAPlayerId = 400;
    private const long ClubBPlayerId = 401;
    private const long ClubATeamId = 500;

    private const long ActiveCampaignId = 600;
    private const long ClosedCampaignId = 601;
    private const long OlderCampaignId = 550;

    private const long ActiveAssignmentId = 700;
    private const long ClosedAssignmentId = 701;
    private const long OlderAssignmentId = 702;
    private const long ClubBAssignmentId = 703;

    private const long LeadershipTagId = 800;
    private const long AgilityTagId = 801;
    private const long ArchivedTagId = 802;

    private const long OlderActiveNoteId = 900;
    private const long NewerActiveNoteId = 901;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes seeded player/history data for two clubs.
    /// </summary>
    public PlayerDetailQueryServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies that player detail returns permanent profile fields plus deterministically ordered campaign,
    /// notes, tag applications, and active-campaign current traits.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsOrderedCampaignHistoryAndCurrentTraits()
    {
        ActAs(ClubAViewerId, ClubAId);
        var service = CreateService();

        var result = await service.GetPlayerDetailAsync(ClubAPlayerId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlayerId.ShouldBe(ClubAPlayerId);
        result.Value.FirstName.ShouldBe("Avery");
        result.Value.LastName.ShouldBe("Archive");
        result.Value.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        result.Value.CampaignHistory.Count.ShouldBe(3);
        result.Value.CampaignHistory.Select(history => history.CampaignId).ToList()
            .ShouldBe([ClosedCampaignId, ActiveCampaignId, OlderCampaignId]);

        var activeCampaign = result.Value.CampaignHistory.Single(history => history.CampaignId == ActiveCampaignId);
        activeCampaign.TryoutNumber.ShouldBe(12);
        activeCampaign.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        activeCampaign.Team.ShouldNotBeNull();
        activeCampaign.Team.TeamId.ShouldBe(ClubATeamId);
        activeCampaign.Team.Name.ShouldBe("Club A Blue");
        activeCampaign.Team.LifecycleStatus.ShouldBe(LifecycleStatus.Active);

        activeCampaign.Notes.Select(note => note.NoteId).ToList().ShouldBe([NewerActiveNoteId, OlderActiveNoteId]);
        activeCampaign.TagApplications.Select(application => application.PlayerTagId).ToList()
            .ShouldBe([AgilityTagId, LeadershipTagId]);

        result.Value.CurrentTraits.Select(trait => trait.PlayerTagId).ToList().ShouldBe([AgilityTagId, LeadershipTagId]);
    }

    /// <summary>
    /// Verifies unresolved note/tag actors use the stable non-sensitive fallback display text.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_UsesFormerMemberFallback_WhenActorCannotBeResolved()
    {
        ActAs(ClubAViewerId, ClubAId);
        var service = CreateService();

        var result = await service.GetPlayerDetailAsync(ClubAPlayerId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var activeCampaign = result.Value.CampaignHistory.Single(history => history.CampaignId == ActiveCampaignId);
        activeCampaign.Notes.Single(note => note.NoteId == NewerActiveNoteId).AuthorDisplayName.ShouldBe("Former member");
        activeCampaign.TagApplications.Single(application => application.PlayerTagId == AgilityTagId).ApplyingUserDisplayName.ShouldBe("Former member");
    }

    /// <summary>
    /// Verifies missing or cross-tenant players return a non-disclosing not-found result.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsNotFound_ForCrossTenantPlayer()
    {
        ActAs(ClubAViewerId, ClubAId);
        var service = CreateService();

        var result = await service.GetPlayerDetailAsync(ClubBPlayerId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies callers without approved club membership are forbidden.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsForbidden_WhenUserIsNotClubMember()
    {
        ActAs(userId: ClubAViewerId, clubId: null);
        var service = CreateService();

        var result = await service.GetPlayerDetailAsync(ClubAPlayerId, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Creates the service under test over the shared SQLite tenancy harness.
    /// </summary>
    /// <returns>The configured player detail query service.</returns>
    private PlayerDetailQueryService CreateService()
    {
        IDbContextFactory<NovaReadDbContext> readDbFactory =
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext);
        return new PlayerDetailQueryService(
            readDbFactory,
            _harness.CurrentUser,
            NullLogger<PlayerDetailQueryService>.Instance);
    }

    /// <summary>
    /// Sets the simulated current user for the next tenant/read context.
    /// </summary>
    /// <param name="userId">The simulated user identifier.</param>
    /// <param name="clubId">The simulated club identifier.</param>
    /// <param name="isClubAdmin"><see langword="true"/> when the simulated user is a club admin.</param>
    private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>
    /// Seeds club, player, campaign, note, and tag-application history across two clubs.
    /// </summary>
    private void Seed()
    {
        using var context = _harness.CreateAdminContext();

        context.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAViewerId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBViewerId });

        context.Users.AddRange(
            new NovaUserEntity { Id = ClubAViewerId, FirstName = "Casey", LastName = "Viewer", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAOtherUserId, FirstName = "Riley", LastName = "Recorder", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBViewerId, FirstName = "Bryn", LastName = "OtherClub", ClubId = ClubBId });

        context.Players.AddRange(
            new PlayerEntity
            {
                PlayerId = ClubAPlayerId,
                FirstName = "Avery",
                LastName = "Archive",
                DateOfBirth = new DateOnly(2010, 4, 12),
                Gender = Gender.Female,
                GraduationYear = 2028,
                JerseyNumber = 7,
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = DateTimeOffset.UtcNow.AddDays(-2),
                ArchivedById = ClubAOtherUserId,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new PlayerEntity
            {
                PlayerId = ClubBPlayerId,
                FirstName = "Taylor",
                LastName = "Tenant",
                DateOfBirth = new DateOnly(2011, 8, 2),
                GraduationYear = 2029,
                ClubId = ClubBId,
                CreatedById = ClubBViewerId
            });

        context.Teams.Add(
            new TeamEntity
            {
                TeamId = ClubATeamId,
                Name = "Club A Blue",
                GraduationYear = 2028,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            });

        context.Seasons.AddRange(
            new SeasonEntity
            {
                SeasonId = 1000,
                Name = "Club A Season",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new SeasonEntity
            {
                SeasonId = 1001,
                Name = "Club B Season",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubBId,
                CreatedById = ClubBViewerId
            });

        context.Campaigns.AddRange(
            new CampaignEntity
            {
                CampaignId = ActiveCampaignId,
                Name = "Active Tryouts",
                StartDate = new DateOnly(2026, 10, 1),
                Status = CampaignStatus.Active,
                SeasonId = 1000,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new CampaignEntity
            {
                CampaignId = ClosedCampaignId,
                Name = "Closed Tryouts",
                StartDate = new DateOnly(2026, 10, 1),
                Status = CampaignStatus.Closed,
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-30),
                ClosedById = ClubAOtherUserId,
                SeasonId = 1000,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new CampaignEntity
            {
                CampaignId = OlderCampaignId,
                Name = "Older Tryouts",
                StartDate = new DateOnly(2026, 9, 1),
                Status = CampaignStatus.Closed,
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-60),
                ClosedById = ClubAOtherUserId,
                SeasonId = 1000,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new CampaignEntity
            {
                CampaignId = 9999,
                Name = "Other Club Campaign",
                StartDate = new DateOnly(2026, 10, 1),
                Status = CampaignStatus.Active,
                SeasonId = 1001,
                ClubId = ClubBId,
                CreatedById = ClubBViewerId
            });

        context.PlayerTags.AddRange(
            new PlayerTagEntity
            {
                PlayerTagId = LeadershipTagId,
                Name = "Leadership",
                Color = "#112233",
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new PlayerTagEntity
            {
                PlayerTagId = AgilityTagId,
                Name = "Agility",
                Color = "#334455",
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new PlayerTagEntity
            {
                PlayerTagId = ArchivedTagId,
                Name = "Archived Tag",
                Color = "#778899",
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = DateTimeOffset.UtcNow.AddDays(-10),
                ArchivedById = ClubAOtherUserId,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            });

        context.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                PlayerId = ClubAPlayerId,
                CampaignId = ActiveCampaignId,
                TryoutNumber = 12,
                PlacementOutcome = PlacementOutcome.Assigned,
                TeamId = ClubATeamId,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ClosedAssignmentId,
                PlayerId = ClubAPlayerId,
                CampaignId = ClosedCampaignId,
                TryoutNumber = null,
                PlacementOutcome = PlacementOutcome.NotSelected,
                TeamId = null,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = OlderAssignmentId,
                PlayerId = ClubAPlayerId,
                CampaignId = OlderCampaignId,
                TryoutNumber = 22,
                PlacementOutcome = PlacementOutcome.Withdrawn,
                TeamId = null,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = ClubBAssignmentId,
                PlayerId = ClubBPlayerId,
                CampaignId = 9999,
                PlacementOutcome = PlacementOutcome.Undecided,
                ClubId = ClubBId,
                CreatedById = ClubBViewerId
            });

        context.Notes.AddRange(
            new NoteEntity
            {
                NoteId = OlderActiveNoteId,
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                Content = "Older note.",
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new NoteEntity
            {
                NoteId = NewerActiveNoteId,
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                Content = "Newest note from a deleted actor.",
                ClubId = ClubAId,
                CreatedById = MissingActorUserId
            },
            new NoteEntity
            {
                NoteId = 902,
                PlayerCampaignAssignmentId = ClosedAssignmentId,
                Content = "Closed campaign note.",
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            });

        context.CampaignTagApplications.AddRange(
            new CampaignTagApplicationEntity
            {
                CampaignTagApplicationId = 10000,
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                PlayerTagId = LeadershipTagId,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new CampaignTagApplicationEntity
            {
                CampaignTagApplicationId = 10001,
                PlayerCampaignAssignmentId = ActiveAssignmentId,
                PlayerTagId = AgilityTagId,
                ClubId = ClubAId,
                CreatedById = MissingActorUserId
            },
            new CampaignTagApplicationEntity
            {
                CampaignTagApplicationId = 10002,
                PlayerCampaignAssignmentId = ClosedAssignmentId,
                PlayerTagId = ArchivedTagId,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            },
            new CampaignTagApplicationEntity
            {
                CampaignTagApplicationId = 10003,
                PlayerCampaignAssignmentId = OlderAssignmentId,
                PlayerTagId = LeadershipTagId,
                ClubId = ClubAId,
                CreatedById = ClubAOtherUserId
            });

        context.SaveChanges();

        var olderNote = context.Notes.Single(note => note.NoteId == OlderActiveNoteId);
        var newerNote = context.Notes.Single(note => note.NoteId == NewerActiveNoteId);
        var leadershipApp = context.CampaignTagApplications.Single(application => application.CampaignTagApplicationId == 10000);
        var agilityApp = context.CampaignTagApplications.Single(application => application.CampaignTagApplicationId == 10001);

        olderNote.CreatedAt = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        newerNote.CreatedAt = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
        leadershipApp.CreatedAt = new DateTimeOffset(2026, 10, 2, 7, 0, 0, TimeSpan.Zero);
        agilityApp.CreatedAt = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

        context.SaveChanges();
    }
}
