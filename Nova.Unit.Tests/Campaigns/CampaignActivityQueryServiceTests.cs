using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Activity;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies the bounded, deterministically ordered recent-activity query, including actor display-name
/// resolution, tenant isolation, authorization, validation, and closed-campaign readability.
/// </summary>
public sealed class CampaignActivityQueryServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAMemberId = 300;
    private const long ClubAAdminId = 301;
    private const long ClubBMemberId = 400;
    private const long MissingActorUserId = 999_999;

    private readonly TenancyTestHarness _harness = new();
    private long _activeCampaignId;
    private long _closedCampaignId;
    private long _campaignBId;

    /// <summary>Initializes seeded club, user, season, and campaign data for two clubs.</summary>
    public CampaignActivityQueryServiceTests() => SeedBase();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>Verifies an unsigned-in caller cannot read activity.</summary>
    [Fact]
    public async Task GetActivity_ReturnsForbidden_WhenNotSignedIn()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = _activeCampaignId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies a signed-in user without a club cannot read activity.</summary>
    [Fact]
    public async Task GetActivity_ReturnsForbidden_WhenUserHasNoClub()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = _activeCampaignId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies invalid campaign identifiers are rejected before any query.</summary>
    [Fact]
    public async Task GetActivity_ReturnsValidation_ForNonPositiveCampaignId()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>Verifies an out-of-range explicit limit is rejected.</summary>
    [Fact]
    public async Task GetActivity_ReturnsValidation_ForOutOfRangeLimit()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = _activeCampaignId, Limit = 51 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>Verifies a missing campaign returns a non-disclosing not-found.</summary>
    [Fact]
    public async Task GetActivity_ReturnsNotFound_ForMissingCampaign()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = 999_999 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies another club's campaign is invisible to the current tenant.</summary>
    [Fact]
    public async Task GetActivity_ReturnsNotFound_ForCrossTenantCampaign()
    {
        _harness.CurrentUser.UserId = ClubBMemberId;
        _harness.CurrentUser.ClubId = ClubBId;

        var result = await CreateService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = _activeCampaignId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies a Closed campaign's activity remains readable.</summary>
    [Fact]
    public async Task GetActivity_ReturnsEvents_ForClosedCampaign()
    {
        var actorUserId = ClubAAdminId;
        var createdAt = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
        SeedEvents(
            _closedCampaignId,
            ClubAId,
            [(CampaignLifecycleEventType.Closed, createdAt, actorUserId, "Admin A")]);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.ShouldHaveSingleItem();
        result.Value.Events[0].EventType.ShouldBe(CampaignLifecycleEventType.Closed);
        result.Value.Events[0].ActorUserId.ShouldBe(actorUserId);
        result.Value.Events[0].ActorDisplayName.ShouldBe("Admin A");
    }

    /// <summary>Verifies the result is bounded to the 50 newest events.</summary>
    [Fact]
    public async Task GetActivity_ReturnsFiftyNewestEvents_WhenMoreThanFiftyExist()
    {
        var baseTime = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var specs = Enumerable.Range(0, 60)
            .Select(index => (
                EventType: index % 2 == 0 ? CampaignLifecycleEventType.Closed : CampaignLifecycleEventType.Reopened,
                CreatedAt: baseTime.AddMinutes(index),
                ActorUserId: ClubAAdminId,
                ActorDisplayName: "Admin A"))
            .ToList();
        var entities = SeedEvents(_activeCampaignId, ClubAId, specs);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = _activeCampaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Count.ShouldBe(GetCampaignActivityInput.MaxEventCount);

        var expectedIds = entities
            .OrderByDescending(entity => entity.CreatedAt)
            .ThenByDescending(entity => entity.ActivityEventId)
            .Take(GetCampaignActivityInput.MaxEventCount)
            .Select(entity => entity.ActivityEventId)
            .ToList();
        result.Value.Events.Select(item => item.CampaignLifecycleEventId).ShouldBe(expectedIds);
    }

    /// <summary>Verifies equal-timestamp events use the identifier descending tie-breaker.</summary>
    [Fact]
    public async Task GetActivity_OrdersEqualTimestamps_ByDescendingEventId()
    {
        var equalTime = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
        var entities = SeedEvents(
            _activeCampaignId,
            ClubAId,
            [
                (CampaignLifecycleEventType.Closed, equalTime, ClubAAdminId, "Admin A"),
                (CampaignLifecycleEventType.Reopened, equalTime, ClubAAdminId, "Admin A")
            ]);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = _activeCampaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Count.ShouldBe(2);
        var expectedIds = entities
            .OrderByDescending(entity => entity.ActivityEventId)
            .Select(entity => entity.ActivityEventId)
            .ToList();
        result.Value.Events.Select(item => item.CampaignLifecycleEventId).ShouldBe(expectedIds);
    }

    /// <summary>Verifies stored actor name snapshots are returned verbatim, even for removed users.</summary>
    [Fact]
    public async Task GetActivity_ReturnsStoredActorSnapshots_ForRemovedUsers()
    {
        SeedEvents(
            _activeCampaignId,
            ClubAId,
            [
                (CampaignLifecycleEventType.Closed, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), ClubAAdminId, "Admin A"),
                (CampaignLifecycleEventType.Reopened, new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero), MissingActorUserId, "Former member")
            ]);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = _activeCampaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var resolved = result.Value.Events.Single(item => item.ActorUserId == ClubAAdminId);
        resolved.ActorDisplayName.ShouldBe("Admin A");
        var missing = result.Value.Events.Single(item => item.ActorUserId == MissingActorUserId);
        missing.ActorDisplayName.ShouldBe("Former member");
    }

    /// <summary>Verifies a requested explicit limit is honored.</summary>
    [Fact]
    public async Task GetActivity_ReturnsOnlyRequestedLimit()
    {
        var baseTime = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var specs = Enumerable.Range(0, 10)
            .Select(index => (
                EventType: CampaignLifecycleEventType.Closed,
                CreatedAt: baseTime.AddMinutes(index),
                ActorUserId: ClubAAdminId,
                ActorDisplayName: "Admin A"))
            .ToList();
        SeedEvents(_activeCampaignId, ClubAId, specs);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = _activeCampaignId, Limit = 5 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Count.ShouldBe(5);
    }

    /// <summary>Creates the closeout query service over the shared SQLite tenancy harness.</summary>
    /// <returns>A service instance using the mutable fake current-user provider.</returns>
    private CampaignCloseoutQueryService CreateService()
        => new(
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            new CampaignPlacementQueryService(
                new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
                _harness.CurrentUser,
                NullLogger<CampaignPlacementQueryService>.Instance),
            NullLogger<CampaignCloseoutQueryService>.Instance);

    /// <summary>Seeds lifecycle events and returns the persisted entities for ordering assertions.</summary>
    /// <param name="campaignId">The owning campaign identifier.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="specs">The event type, timestamp, actor, and stored name snapshot for each event.</param>
    /// <returns>The persisted event entities in seed order.</returns>
    private IReadOnlyList<ActivityEventEntity> SeedEvents(
        long campaignId,
        long clubId,
        IReadOnlyList<(CampaignLifecycleEventType EventType, DateTimeOffset CreatedAt, long ActorUserId, string ActorDisplayName)> specs)
    {
        using var admin = _harness.CreateAdminContext();

        // Straight SQL because the tenant interceptor re-stamps CreatedAt to now on Added, which
        // would discard the caller-supplied occurrence time the ordering assertions rely on.
        var connection = admin.Database.GetDbConnection();
        foreach (var spec in specs)
        {
            var eventKind = spec.EventType == CampaignLifecycleEventType.Closed
                ? ActivityEventKind.CampaignClosed
                : ActivityEventKind.CampaignReopened;
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO "ActivityEvents"
                    ("ClubId", "CampaignId", "EventKind", "IsAdminOnly", "ActorUserId",
                     "ActorDisplayName", "PayloadJson", "CreatedAt", "CreatedById")
                VALUES
                    (@clubId, @campaignId, @eventKind, @isAdminOnly, @actorUserId,
                     @actorDisplayName, @payloadJson, @createdAt, @createdById)
                """;
            AddParameter(command, "@clubId", clubId);
            AddParameter(command, "@campaignId", campaignId);
            AddParameter(command, "@eventKind", (int)eventKind);
            AddParameter(command, "@isAdminOnly", ActivityEventPolicy.IsAdminOnly(eventKind) ? 1 : 0);
            AddParameter(command, "@actorUserId", spec.ActorUserId);
            AddParameter(command, "@actorDisplayName", spec.ActorDisplayName);
            AddParameter(command, "@payloadJson", JsonSerializer.Serialize(
                new CampaignLifecycleContext
                {
                    CampaignId = campaignId,
                    CampaignName = "Active A",
                },
                typeof(ClubActivityContext),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            AddParameter(command, "@createdAt", spec.CreatedAt);
            AddParameter(command, "@createdById", spec.ActorUserId);
            command.ExecuteNonQuery();
        }

        return admin.ActivityEvents
            .AsNoTracking()
            .OrderBy(entity => entity.ActivityEventId)
            .ToList();
    }

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Seeds clubs, users, seasons, and campaigns for two clubs.</summary>
    private void SeedBase()
    {
        using var admin = _harness.CreateAdminContext();

        admin.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });
        admin.Users.AddRange(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Amelia", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bobby", LastName = "Member", ClubId = ClubBId });
        admin.Seasons.AddRange(
            new SeasonEntity { CreationOperationId = Guid.NewGuid(), SeasonId = 500, Name = "Season A", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubAId, CreatedById = ClubAMemberId },
            new SeasonEntity { CreationOperationId = Guid.NewGuid(), SeasonId = 501, Name = "Season B", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubBId, CreatedById = ClubBMemberId });
        admin.Campaigns.AddRange(
            new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Active A", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = 500, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Closed A", StartDate = new DateOnly(2026, 5, 1), Status = CampaignStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedById = ClubAAdminId, SeasonId = 500, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Campaign B", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = 501, ClubId = ClubBId, CreatedById = ClubBMemberId });
        admin.SaveChanges();

        using var read = _harness.CreateAdminContext();
        _activeCampaignId = read.Campaigns.Single(campaign => campaign.Name == "Active A").CampaignId;
        _closedCampaignId = read.Campaigns.Single(campaign => campaign.Name == "Closed A").CampaignId;
        _campaignBId = read.Campaigns.Single(campaign => campaign.Name == "Campaign B").CampaignId;
    }
}
