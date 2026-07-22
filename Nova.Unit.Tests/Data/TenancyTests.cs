using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Interceptors;
using Nova.Data.Tenancy;
using Nova.Entities;
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
        new(Options<NovaDbContext>(withInterceptor: true), CurrentUser);

    public NovaReadDbContext CreateReadContext() =>
        new(Options<NovaReadDbContext>(withInterceptor: false), CurrentUser);

    public NovaAdminDbContext CreateAdminContext() =>
        new(Options<NovaAdminDbContext>(withInterceptor: true), CurrentUser);

    public void Dispose() => _connection.Dispose();

    private DbContextOptions<TContext> Options<TContext>(bool withInterceptor) where TContext : DbContext
    {
        // Attach the pinned Identity options so the model matches the running app.
        var builder = new DbContextOptionsBuilder<TContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance);
        if (withInterceptor)
        {
            builder.AddInterceptors(new TenantSaveChangesInterceptor());
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
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = NoClubUserId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = NoClubUserId });

        context.Users.AddRange(
            new NovaUserEntity { Id = ClubAMember1Id, FirstName = "Alice", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMember2Id, FirstName = "Aaron", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bob", LastName = "B", ClubId = ClubBId },
            new NovaUserEntity { Id = NoClubUserId, FirstName = "Nadia", LastName = "N", ClubId = null });

        context.Players.AddRange(
            new PlayerEntity { FirstName = "PA", LastName = "One", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, ClubId = ClubAId, CreatedById = ClubAMember1Id },
            new PlayerEntity { FirstName = "PA", LastName = "Two", DateOfBirth = new DateOnly(2011, 2, 2), GraduationYear = 2029, ClubId = ClubAId, CreatedById = ClubAMember1Id },
            new PlayerEntity { FirstName = "PB", LastName = "One", DateOfBirth = new DateOnly(2012, 3, 3), GraduationYear = 2030, ClubId = ClubBId, CreatedById = ClubBMemberId });

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

        // Seed seasons, campaigns, and participations so notes can be associated per-club.
        var seasonA = new SeasonEntity
        {
            Name = "Season A",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        var seasonB = new SeasonEntity
        {
            Name = "Season B",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = ClubBId,
            CreatedById = ClubBMemberId
        };
        context.Seasons.AddRange(seasonA, seasonB);
        context.SaveChanges();

        var campaignA = new CampaignEntity
        {
            Name = "Campaign A",
            StartDate = new DateOnly(2026, 6, 1),
            SeasonId = seasonA.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        var campaignB = new CampaignEntity
        {
            Name = "Campaign B",
            StartDate = new DateOnly(2026, 6, 1),
            SeasonId = seasonB.SeasonId,
            ClubId = ClubBId,
            CreatedById = ClubBMemberId
        };
        context.Campaigns.AddRange(campaignA, campaignB);
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
        context.PlayerCampaignAssignments.AddRange(assignmentA, assignmentB);
        context.SaveChanges();

        _clubAAssignmentId = assignmentA.PlayerCampaignAssignmentId;
        _clubBAssignmentId = assignmentB.PlayerCampaignAssignmentId;

        // One note per club to exercise the tenant filter on NoteEntity.
        context.Notes.AddRange(
            new NoteEntity { Content = "Note A", PlayerCampaignAssignmentId = _clubAAssignmentId, ClubId = ClubAId, CreatedById = ClubAMember1Id },
            new NoteEntity { Content = "Note B", PlayerCampaignAssignmentId = _clubBAssignmentId, ClubId = ClubBId, CreatedById = ClubBMemberId });
        context.SaveChanges();
    }

    private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isClubAdmin;
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
            Content = "Cross-tenant attempt.",
            PlayerCampaignAssignmentId = _clubBAssignmentId,
            ClubId = ClubBId,
            CreatedById = ClubAMember1Id
        });

        Should.Throw<InvalidOperationException>(() => context.SaveChanges())
            .Message.ShouldContain("Cross-tenant");
    }
}
