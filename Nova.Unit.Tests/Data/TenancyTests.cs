using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nova.Data;
using Nova.Data.Interceptors;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Shared.Enums;
using Nova.Shared.Security;
using Shouldly;

namespace Nova.Unit.Tests.Data;

/// <summary>
/// A mutable <see cref="ICurrentUserProvider"/> for simulating different users in tests.
/// </summary>
public sealed class FakeCurrentUserProvider : ICurrentUserProvider
{
    public long? UserId { get; set; }
    public long? ClubId { get; set; }
    public bool IsClubAdmin { get; set; }

    public CurrentUserState GetCurrentUserState() =>
        (UserId, ClubId) switch
        {
            (null, _) => new Anonymous(),
            ({ } userId, null) => new AuthenticatedUser(userId),
            ({ } userId, { } clubId) => new ClubMember(userId, clubId, IsClubAdmin),
        };
}

/// <summary>
/// Creates the three application contexts over a shared in-memory Sqlite database.
/// </summary>
public sealed class TenancyTestHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    public FakeCurrentUserProvider CurrentUser { get; } = new();

    public TenancyTestHarness()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateAdminContext();
        context.Database.EnsureCreated();
    }

    public NovaDbContext CreateTenantContext() =>
        new(Options<NovaDbContext>(withTenantInterceptor: true), CurrentUser);

    public NovaReadDbContext CreateReadContext() =>
        new(Options<NovaReadDbContext>(withTenantInterceptor: false), CurrentUser);

    public NovaReadDbContext CreateReadContext(IInterceptor interceptor) =>
        new(Options<NovaReadDbContext>(withTenantInterceptor: false, interceptor), CurrentUser);

    public NovaAdminDbContext CreateAdminContext() =>
        new(Options<NovaAdminDbContext>(withTenantInterceptor: true), CurrentUser);

    public void Dispose() => _connection.Dispose();

    private DbContextOptions<TContext> Options<TContext>(
        bool withTenantInterceptor,
        params IInterceptor[] interceptors)
        where TContext : DbContext
    {
        // Attach the pinned Identity options so the model matches the running app.
        var builder = new DbContextOptionsBuilder<TContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance);
        if (withTenantInterceptor)
        {
            builder.AddInterceptors(new TenantSaveChangesInterceptor());
        }

        if (interceptors.Length > 0)
        {
            builder.AddInterceptors(interceptors);
        }

        return builder.Options;
    }
}

public class TenancyTests : IDisposable
{
    private const long ClubAId = 1;
    private const long ClubBId = 2;
    private const long ClubAMember1Id = 10;
    private const long ClubAMember2Id = 11;
    private const long ClubBMemberId = 12;
    private const long NoClubUserId = 13;

    private readonly TenancyTestHarness _harness = new();

    // Assigned during Seed() once database-generated IDs are available.
    private long _clubAAssignmentId;
    private long _clubBAssignmentId;
    private long _clubADraftAssignmentId;
    private long _clubBDraftAssignmentId;

    public TenancyTests()
    {
        Seed();
    }

    public void Dispose() => _harness.Dispose();

    private void Seed()
    {
        // Admin context bypasses tenant guarding, allowing cross-tenant seeding.
        using var context = _harness.CreateAdminContext();

        context.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = NoClubUserId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = NoClubUserId });

        context.Users.AddRange(
            new NovaUserEntity { Id = ClubAMember1Id, FirstName = "Alice", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMember2Id, FirstName = "Aaron", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bob", LastName = "B", ClubId = ClubBId },
            new NovaUserEntity { Id = NoClubUserId, FirstName = "Nadia", LastName = "N", ClubId = null });

        context.Players.AddRange(
            new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "PA", LastName = "One", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, ClubId = ClubAId, CreatedById = ClubAMember1Id },
            new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "PA", LastName = "Two", DateOfBirth = new DateOnly(2011, 2, 2), GraduationYear = 2029, ClubId = ClubAId, CreatedById = ClubAMember1Id },
            new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "PB", LastName = "One", DateOfBirth = new DateOnly(2012, 3, 3), GraduationYear = 2030, ClubId = ClubBId, CreatedById = ClubBMemberId });

        // Pending request from the club-less user to join Club A.
        context.ClubJoinRequests.Add(
            new ClubJoinRequestEntity { ClubId = ClubAId, RequestingUserId = NoClubUserId, CreatedById = NoClubUserId });

        // One photo per user, to exercise the navigation-based photo filter.
        context.NovaUserPhotos.AddRange(
            new NovaUserPhotoEntity { OriginalBlobName = "a1.jpg", NovaUserId = ClubAMember1Id, CreatedById = ClubAMember1Id },
            new NovaUserPhotoEntity { OriginalBlobName = "a2.jpg", NovaUserId = ClubAMember2Id, CreatedById = ClubAMember2Id },
            new NovaUserPhotoEntity { OriginalBlobName = "b1.jpg", NovaUserId = ClubBMemberId, CreatedById = ClubBMemberId },
            new NovaUserPhotoEntity { OriginalBlobName = "n1.jpg", NovaUserId = NoClubUserId, CreatedById = NoClubUserId });

        context.SaveChanges();

        context.ClubMembershipMutationReceipts.AddRange(
            new ClubMembershipMutationReceiptEntity
            {
                OperationId = Guid.NewGuid(),
                MemberUserId = ClubAMember2Id,
                MutationKind = "Promote",
                ClubId = ClubAId,
                CreatedById = ClubAMember1Id,
            },
            new ClubMembershipMutationReceiptEntity
            {
                OperationId = Guid.NewGuid(),
                MemberUserId = ClubBMemberId,
                MutationKind = "Demote",
                ClubId = ClubBId,
                CreatedById = ClubBMemberId,
            });
        context.SaveChanges();

        // Seed seasons, campaigns, and participations so notes can be associated per-club.
        var seasonA = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Season A",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        var seasonB = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Season B",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = ClubBId,
            CreatedById = ClubBMemberId
        };
        context.Seasons.AddRange(seasonA, seasonB);
        context.SaveChanges();

        var campaignA = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Campaign A",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = seasonA.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        var campaignB = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Campaign B",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = seasonB.SeasonId,
            ClubId = ClubBId,
            CreatedById = ClubBMemberId
        };
        context.Campaigns.AddRange(campaignA, campaignB);
        context.SaveChanges();

        var draftCampaignA = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Draft Campaign A",
            StartDate = new DateOnly(2026, 7, 1),
            Status = CampaignStatus.Draft,
            SeasonId = seasonA.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        var draftCampaignB = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Draft Campaign B",
            StartDate = new DateOnly(2026, 7, 1),
            Status = CampaignStatus.Draft,
            SeasonId = seasonB.SeasonId,
            ClubId = ClubBId,
            CreatedById = ClubBMemberId
        };
        context.Campaigns.AddRange(draftCampaignA, draftCampaignB);
        context.ActivityEvents.AddRange(
            CreateActivity(ClubAId, isAdminOnly: false, "Member A"),
            CreateActivity(ClubAId, isAdminOnly: true, "Admin A"),
            CreateActivity(ClubBId, isAdminOnly: false, "Member B"),
            CreateActivity(ClubBId, isAdminOnly: true, "Admin B"));
        context.SaveChanges();

        var playerA = context.Players.First(p => p.ClubId == ClubAId);
        var playerB = context.Players.First(p => p.ClubId == ClubBId);
        var assignmentA = new PlayerCampaignAssignmentEntity
        {
            PlayerId = playerA.PlayerId,
            CampaignId = campaignA.CampaignId,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        var assignmentB = new PlayerCampaignAssignmentEntity
        {
            PlayerId = playerB.PlayerId,
            CampaignId = campaignB.CampaignId,
            ClubId = ClubBId,
            CreatedById = ClubBMemberId
        };
        var draftAssignmentA = new PlayerCampaignAssignmentEntity
        {
            PlayerId = playerA.PlayerId,
            CampaignId = draftCampaignA.CampaignId,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        var draftAssignmentB = new PlayerCampaignAssignmentEntity
        {
            PlayerId = playerB.PlayerId,
            CampaignId = draftCampaignB.CampaignId,
            ClubId = ClubBId,
            CreatedById = ClubBMemberId
        };
        context.PlayerCampaignAssignments.AddRange(
            assignmentA,
            assignmentB,
            draftAssignmentA,
            draftAssignmentB);
        context.SaveChanges();

        _clubAAssignmentId = assignmentA.PlayerCampaignAssignmentId;
        _clubBAssignmentId = assignmentB.PlayerCampaignAssignmentId;
        _clubADraftAssignmentId = draftAssignmentA.PlayerCampaignAssignmentId;
        _clubBDraftAssignmentId = draftAssignmentB.PlayerCampaignAssignmentId;

        // Active and Draft evaluation rows exercise role-shaped visibility on the dependent graph.
        context.Notes.AddRange(
            new NoteEntity { CreationOperationId = Guid.NewGuid(), Content = "Note A", PlayerCampaignAssignmentId = _clubAAssignmentId, ClubId = ClubAId, CreatedById = ClubAMember1Id },
            new NoteEntity { CreationOperationId = Guid.NewGuid(), Content = "Note B", PlayerCampaignAssignmentId = _clubBAssignmentId, ClubId = ClubBId, CreatedById = ClubBMemberId },
            new NoteEntity { CreationOperationId = Guid.NewGuid(), Content = "Draft Note A", PlayerCampaignAssignmentId = _clubADraftAssignmentId, ClubId = ClubAId, CreatedById = ClubAMember1Id },
            new NoteEntity { CreationOperationId = Guid.NewGuid(), Content = "Draft Note B", PlayerCampaignAssignmentId = _clubBDraftAssignmentId, ClubId = ClubBId, CreatedById = ClubBMemberId });

        var tagA = new PlayerTagEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Tag A",
            NormalizedName = "TAG A",
            Color = "#112233",
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        var tagB = new PlayerTagEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Tag B",
            NormalizedName = "TAG B",
            Color = "#445566",
            ClubId = ClubBId,
            CreatedById = ClubBMemberId
        };
        context.PlayerTags.AddRange(tagA, tagB);
        context.SaveChanges();

        context.CampaignTagApplications.AddRange(
            new CampaignTagApplicationEntity { CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = _clubAAssignmentId, PlayerTagId = tagA.PlayerTagId, ClubId = ClubAId, CreatedById = ClubAMember1Id },
            new CampaignTagApplicationEntity { CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = _clubADraftAssignmentId, PlayerTagId = tagA.PlayerTagId, ClubId = ClubAId, CreatedById = ClubAMember1Id },
            new CampaignTagApplicationEntity { CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = _clubBAssignmentId, PlayerTagId = tagB.PlayerTagId, ClubId = ClubBId, CreatedById = ClubBMemberId },
            new CampaignTagApplicationEntity { CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = _clubBDraftAssignmentId, PlayerTagId = tagB.PlayerTagId, ClubId = ClubBId, CreatedById = ClubBMemberId });
        context.SaveChanges();
    }

    private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    private static ActivityEventEntity CreateActivity(long clubId, bool isAdminOnly, string actorName)
        => new()
        {
            ClubId = clubId,
            EventKind = ActivityEventKind.MemberJoined,
            IsAdminOnly = isAdminOnly,
            ActorUserId = clubId == ClubAId ? ClubAMember1Id : ClubBMemberId,
            ActorDisplayName = actorName,
            PayloadJson = "{}",
            CreatedById = clubId == ClubAId ? ClubAMember1Id : ClubBMemberId
        };

    [Fact]
    public void Campaigns_HideDraftsFromMembers_AndShowThemToClubAdmins()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using (var member = _harness.CreateReadContext())
        {
            member.Campaigns.Select(campaign => campaign.Name).ShouldBe(["Campaign A"]);
        }

        ActAs(ClubAMember1Id, ClubAId, isClubAdmin: true);
        using var admin = _harness.CreateReadContext();
        admin.Campaigns.Select(campaign => campaign.Name)
            .ShouldBe(["Campaign A", "Draft Campaign A"], ignoreOrder: true);
    }

    [Fact]
    public void DraftEvaluationGraph_IsVisibleOnlyToOwningClubAdministratorsAndAdminContext()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using (var member = _harness.CreateReadContext())
        {
            member.PlayerCampaignAssignments.Select(assignment => assignment.PlayerCampaignAssignmentId)
                .ShouldBe([_clubAAssignmentId]);
            member.Notes.Select(note => note.Content).ShouldBe(["Note A"]);
            member.CampaignTagApplications.Count().ShouldBe(1);
        }

        ActAs(ClubAMember1Id, ClubAId, isClubAdmin: true);
        using (var clubAdmin = _harness.CreateReadContext())
        {
            clubAdmin.PlayerCampaignAssignments.Select(assignment => assignment.PlayerCampaignAssignmentId)
                .ShouldBe([_clubAAssignmentId, _clubADraftAssignmentId], ignoreOrder: true);
            clubAdmin.Notes.Select(note => note.Content)
                .ShouldBe(["Note A", "Draft Note A"], ignoreOrder: true);
            clubAdmin.CampaignTagApplications.Count().ShouldBe(2);
        }

        ActAs(ClubBMemberId, ClubBId);
        using (var otherClubMember = _harness.CreateReadContext())
        {
            otherClubMember.PlayerCampaignAssignments.Select(assignment => assignment.PlayerCampaignAssignmentId)
                .ShouldBe([_clubBAssignmentId]);
            otherClubMember.Notes.Select(note => note.Content).ShouldBe(["Note B"]);
            otherClubMember.CampaignTagApplications.Count().ShouldBe(1);
        }

        using var allClubs = _harness.CreateAdminContext();
        allClubs.PlayerCampaignAssignments.Count().ShouldBe(4);
        allClubs.Notes.Count().ShouldBe(4);
        allClubs.CampaignTagApplications.Count().ShouldBe(4);
    }

    [Fact]
    public void ActivityEvents_HideAdminOnlyRowsFromMembers_AndPreserveTenantIsolation()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using (var member = _harness.CreateReadContext())
        {
            member.ActivityEvents.Select(activity => activity.ActorDisplayName).ShouldBe(["Member A"]);
        }

        ActAs(ClubAMember1Id, ClubAId, isClubAdmin: true);
        using (var clubAdmin = _harness.CreateReadContext())
        {
            clubAdmin.ActivityEvents.Select(activity => activity.ActorDisplayName)
                .ShouldBe(["Member A", "Admin A"], ignoreOrder: true);
        }

        using var allClubs = _harness.CreateAdminContext();
        allClubs.ActivityEvents.Count().ShouldBe(4);
    }

    [Fact]
    public void TenantContext_ReturnsOnlyCurrentClubsRows()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        var players = context.Players.ToList();

        players.Count.ShouldBe(2);
        players.ShouldAllBe(p => p.ClubId == ClubAId);
    }

    [Fact]
    public void TenantContext_UserWithoutClub_SeesNoTenantData()
    {
        ActAs(NoClubUserId, clubId: null);
        using var context = _harness.CreateTenantContext();

        context.Players.Count().ShouldBe(0);
    }

    [Fact]
    public void TenantContext_ClubsAreUnfiltered()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        context.Clubs.Count().ShouldBe(2);
    }

    [Fact]
    public void ClubMembershipMutationReceipts_VisibleOnlyToOwningClub()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        var receipts = context.ClubMembershipMutationReceipts.ToList();

        receipts.Count.ShouldBe(1);
        receipts[0].ClubId.ShouldBe(ClubAId);
    }

    [Fact]
    public void ClubMembershipMutationReceipts_HiddenFromOtherClub()
    {
        ActAs(ClubBMemberId, ClubBId);
        using var context = _harness.CreateTenantContext();

        var receipts = context.ClubMembershipMutationReceipts.ToList();

        receipts.Count.ShouldBe(1);
        receipts[0].ClubId.ShouldBe(ClubBId);
    }

    [Fact]
    public void Interceptor_Throws_OnCrossTenantClubMembershipMutationReceiptAdd()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();
        context.ClubMembershipMutationReceipts.Add(new ClubMembershipMutationReceiptEntity
        {
            OperationId = Guid.NewGuid(),
            MemberUserId = ClubBMemberId,
            MutationKind = "Remove",
            ClubId = ClubBId,
            CreatedById = ClubAMember1Id,
        });

        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain("Cross-tenant");
    }

    [Fact]
    public void AdminContext_BypassesTenantFilters()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateAdminContext();

        context.Players.Count().ShouldBe(3);
        context.Users.Count().ShouldBe(4);
        context.ClubJoinRequests.Count().ShouldBe(1);
    }

    [Fact]
    public void ReadContext_AppliesTenantFilters_AndDoesNotTrack()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateReadContext();

        var players = context.Players.ToList();

        players.Count.ShouldBe(2);
        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadContext_AllSaveOverloads_Throw()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateReadContext();

        Should.Throw<InvalidOperationException>(() => context.SaveChanges());
        Should.Throw<InvalidOperationException>(() => context.SaveChanges(acceptAllChangesOnSuccess: true));
        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync(acceptAllChangesOnSuccess: true));
    }

    [Fact]
    public void JoinRequests_VisibleToRequester()
    {
        ActAs(NoClubUserId, clubId: null);
        using var context = _harness.CreateTenantContext();

        var requests = context.ClubJoinRequests.ToList();

        requests.Count.ShouldBe(1);
        requests[0].RequestingUserId.ShouldBe(NoClubUserId);
    }

    [Fact]
    public void JoinRequests_VisibleToTargetClubAdmin()
    {
        ActAs(ClubAMember1Id, ClubAId, isClubAdmin: true);
        using var context = _harness.CreateTenantContext();

        context.ClubJoinRequests.Count().ShouldBe(1);
    }

    [Fact]
    public void JoinRequests_HiddenFromNonAdminClubMember()
    {
        ActAs(ClubAMember1Id, ClubAId, isClubAdmin: false);
        using var context = _harness.CreateTenantContext();

        context.ClubJoinRequests.Count().ShouldBe(0);
    }

    [Fact]
    public void JoinRequests_HiddenFromOtherClubsAdmin()
    {
        ActAs(ClubBMemberId, ClubBId, isClubAdmin: true);
        using var context = _harness.CreateTenantContext();

        context.ClubJoinRequests.Count().ShouldBe(0);
    }

    [Fact]
    public void Users_MemberSeesClubmatesAndSelf()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        var userIds = context.Users.Select(u => u.Id).ToList();

        userIds.ShouldBe([ClubAMember1Id, ClubAMember2Id], ignoreOrder: true);
    }

    [Fact]
    public void Users_ClubLessUserSeesOnlySelf()
    {
        ActAs(NoClubUserId, clubId: null);
        using var context = _harness.CreateTenantContext();

        var userIds = context.Users.Select(u => u.Id).ToList();

        userIds.ShouldBe([NoClubUserId]);
    }

    [Fact]
    public void UserPhotos_MemberSeesClubmatesPhotosAndOwn()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        var ownerIds = context.NovaUserPhotos.Select(p => p.NovaUserId).ToList();

        ownerIds.ShouldBe([ClubAMember1Id, ClubAMember2Id], ignoreOrder: true);
    }

    [Fact]
    public void UserPhotos_ClubLessUserSeesOnlyOwnPhoto()
    {
        ActAs(NoClubUserId, clubId: null);
        using var context = _harness.CreateTenantContext();

        var ownerIds = context.NovaUserPhotos.Select(p => p.NovaUserId).ToList();

        ownerIds.ShouldBe([NoClubUserId]);
    }

    [Fact]
    public void Interceptor_StampsClubIdAndAuditFields_OnAdd()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "New",
            LastName = "Player",
            DateOfBirth = new DateOnly(2013, 4, 4),
            GraduationYear = 2031,
            ClubId = default,
            CreatedById = default,
        };
        context.Players.Add(player);
        context.SaveChanges();

        player.ClubId.ShouldBe(ClubAId);
        player.CreatedById.ShouldBe(ClubAMember1Id);
        player.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void Interceptor_Throws_OnCrossTenantAdd()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        context.Players.Add(new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Sneaky",
            LastName = "Player",
            DateOfBirth = new DateOnly(2013, 5, 5),
            GraduationYear = 2031,
            ClubId = ClubBId,
            CreatedById = ClubAMember1Id,
        });

        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain("Cross-tenant");
    }

    [Fact]
    public void Interceptor_Throws_OnCrossTenantClubIdReassignment()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        var player = context.Players.OrderBy(p => p.PlayerId).First();
        player.ClubId = ClubBId;

        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain("Cross-tenant");
    }

    [Fact]
    public void Interceptor_Throws_OnCrossTenantDelete()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        // The filter hides Club B's rows, so simulate a stale/forged reference being deleted.
        long clubBPlayerId;
        using (var admin = _harness.CreateAdminContext())
        {
            clubBPlayerId = admin.Players.Single(p => p.ClubId == ClubBId).PlayerId;
        }

        context.Players.Remove(new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            PlayerId = clubBPlayerId,
            FirstName = "PB",
            LastName = "One",
            DateOfBirth = new DateOnly(2012, 3, 3),
            GraduationYear = 2030,
            ClubId = ClubBId,
            CreatedById = ClubBMemberId,
        });

        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain("Cross-tenant");
    }

    [Fact]
    public void Interceptor_Throws_WhenUserHasNoClub()
    {
        ActAs(NoClubUserId, clubId: null);
        using var context = _harness.CreateTenantContext();

        context.Players.Add(new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Orphan",
            LastName = "Player",
            DateOfBirth = new DateOnly(2013, 6, 6),
            GraduationYear = 2031,
            ClubId = default,
            CreatedById = default,
        });

        Should.Throw<InvalidOperationException>(() => context.SaveChanges());
    }

    [Fact]
    public void Interceptor_StampsModifiedFields_OnUpdate()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        var player = context.Players.First();
        player.JerseyNumber = 42;
        context.SaveChanges();

        player.ModifiedAt.ShouldNotBeNull();
        player.ModifiedById.ShouldBe(ClubAMember1Id);
    }

    [Fact]
    public void Notes_VisibleToOwningClubMember()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        var notes = context.Notes.ToList();

        notes.Count.ShouldBe(1);
        notes[0].ClubId.ShouldBe(ClubAId);
    }

    [Fact]
    public void Notes_HiddenFromOtherClub()
    {
        ActAs(ClubBMemberId, ClubBId);
        using var context = _harness.CreateTenantContext();

        var notes = context.Notes.ToList();

        notes.Count.ShouldBe(1);
        notes.ShouldAllBe(n => n.ClubId == ClubBId);
    }

    [Fact]
    public void Interceptor_Throws_OnCrossTenantNoteAdd()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        context.Notes.Add(new NoteEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Content = "Cross-tenant attempt.",
            PlayerCampaignAssignmentId = _clubBAssignmentId,
            ClubId = ClubBId,
            CreatedById = ClubAMember1Id
        });

        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain("Cross-tenant");
    }

    [Fact]
    public void Interceptor_AllowsClubLessUser_ToWriteJoinRequestSubmittedActivityEvent()
    {
        ActAs(NoClubUserId, clubId: null);
        using var context = _harness.CreateTenantContext();

        context.ActivityEvents.Add(new ActivityEventEntity
        {
            ClubId = ClubAId,
            EventKind = ActivityEventKind.JoinRequestSubmitted,
            IsAdminOnly = true,
            ActorUserId = NoClubUserId,
            ActorDisplayName = "Orphan User",
            PayloadJson = "{}",
            CreatedById = NoClubUserId,
        });

        // The club-less requester can write the join-request activity row for the club they
        // are requesting to join; the explicit ClubId and join-request kind are the carve-out.
        context.SaveChanges();
    }

    [Fact]
    public void Interceptor_StillGuardsClubLessUser_WhenWritingOtherActivityEventKinds()
    {
        ActAs(NoClubUserId, clubId: null);
        using var context = _harness.CreateTenantContext();

        context.ActivityEvents.Add(new ActivityEventEntity
        {
            ClubId = ClubAId,
            EventKind = ActivityEventKind.MemberJoined,
            IsAdminOnly = false,
            ActorUserId = NoClubUserId,
            ActorDisplayName = "Orphan User",
            PayloadJson = "{}",
            CreatedById = NoClubUserId,
        });

        // The carve-out is scoped to the join-request kinds; any other tenant-owned row
        // written by a club-less user must still be rejected.
        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain("Cross-tenant");
    }

    [Fact]
    public void Interceptor_GuardsClubMember_WhenWritingJoinRequestSubmittedEventForAnotherClub()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();

        context.ActivityEvents.Add(new ActivityEventEntity
        {
            ClubId = ClubBId,
            EventKind = ActivityEventKind.JoinRequestSubmitted,
            IsAdminOnly = true,
            ActorUserId = ClubAMember1Id,
            ActorDisplayName = "Club A Member",
            PayloadJson = "{}",
            CreatedById = ClubAMember1Id,
        });

        // A club member is not a club-less requester; the carve-out does not apply, so a
        // join-request row for a different club is still a cross-tenant write.
        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain("Cross-tenant");
    }

    [Fact]
    public void Interceptor_Throws_OnExistingActivityEventUpdate()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var admin = _harness.CreateAdminContext();
        var activity = new ActivityEventEntity
        {
            ClubId = ClubAId,
            EventKind = ActivityEventKind.MemberJoined,
            IsAdminOnly = false,
            ActorUserId = ClubAMember1Id,
            ActorDisplayName = "Alice A",
            PayloadJson = "{}",
            CreatedById = ClubAMember1Id,
        };
        admin.ActivityEvents.Add(activity);
        admin.SaveChanges();

        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();
        var tracked = context.ActivityEvents.Single(row => row.ActivityEventId == activity.ActivityEventId);
        tracked.ActorDisplayName = "Alice A. Renamed";

        // Activity events are append-only history; a tenant-context update is rejected before
        // the change is written.
        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain("append-only");
    }

    [Fact]
    public void Interceptor_Throws_OnExistingActivityEventDelete()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var admin = _harness.CreateAdminContext();
        var activity = new ActivityEventEntity
        {
            ClubId = ClubAId,
            EventKind = ActivityEventKind.MemberJoined,
            IsAdminOnly = false,
            ActorUserId = ClubAMember1Id,
            ActorDisplayName = "Alice A",
            PayloadJson = "{}",
            CreatedById = ClubAMember1Id,
        };
        admin.ActivityEvents.Add(activity);
        admin.SaveChanges();

        ActAs(ClubAMember1Id, ClubAId);
        using var context = _harness.CreateTenantContext();
        var tracked = context.ActivityEvents.Single(row => row.ActivityEventId == activity.ActivityEventId);
        context.ActivityEvents.Remove(tracked);

        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain("append-only");
    }

    [Fact]
    public void Interceptor_Throws_OnAdminContextActivityEventDelete()
    {
        ActAs(ClubAMember1Id, ClubAId);
        using var admin = _harness.CreateAdminContext();
        var activity = new ActivityEventEntity
        {
            ClubId = ClubAId,
            EventKind = ActivityEventKind.MemberJoined,
            IsAdminOnly = false,
            ActorUserId = ClubAMember1Id,
            ActorDisplayName = "Alice A",
            PayloadJson = "{}",
            CreatedById = ClubAMember1Id,
        };
        admin.ActivityEvents.Add(activity);
        admin.SaveChanges();

        using var deleteContext = _harness.CreateAdminContext();
        var tracked = deleteContext.ActivityEvents.Single(row => row.ActivityEventId == activity.ActivityEventId);
        deleteContext.ActivityEvents.Remove(tracked);

        // The append-only guard applies to admin contexts too: an administrator may not rewrite
        // or delete published history, only append new rows.
        Should.Throw<InvalidOperationException>(() => deleteContext.SaveChanges())
            .Message.ShouldContain("append-only");
    }
}
