using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Seasons;
using Nova.Shared.Enums;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Seasons;

/// <summary>Verifies current-season commands with the SQLite tenancy harness.</summary>
public sealed class SeasonCommandServiceTests : IDisposable
{
    private const long ClubId = 100;
    private const long AdminId = 200;
    private const long CurrentSeasonId = 500;
    private readonly TenancyTestHarness _harness = new();

    /// <summary>Initializes the admin identity and a club with one current season.</summary>
    public SeasonCommandServiceTests()
    {
        _harness.CurrentUser.UserId = AdminId;
        _harness.CurrentUser.ClubId = ClubId;
        _harness.CurrentUser.IsClubAdmin = true;
        using var db = _harness.CreateAdminContext();
        var club = new ClubEntity
        {
            ClubId = ClubId,
            CreationOperationId = Guid.NewGuid(),
            Name = "Club",
            City = "Austin",
            State = "TX",
            CreatedById = AdminId
        };
        db.Clubs.Add(club);
        db.Seasons.Add(new SeasonEntity
        {
            SeasonId = CurrentSeasonId,
            CreationOperationId = Guid.NewGuid(),
            Name = "Current",
            StartDate = new DateOnly(2026, 1, 1),
            ConcurrencyToken = Guid.NewGuid(),
            ClubId = ClubId,
            CreatedById = AdminId
        });
        db.SaveChanges();
        club.CurrentSeasonId = CurrentSeasonId;
        db.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>Verifies standalone creation rejects a club that already has a current season.</summary>
    [Fact]
    public async Task CreateAsync_ReturnsConflict_WhenCurrentSeasonExists()
    {
        var result = await CreateService().CreateAsync(
            new CreateSeasonInput
            {
                OperationId = Guid.NewGuid(),
                Name = "Another",
                StartDate = new DateOnly(2027, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>Verifies every season mutation requires a club administrator.</summary>
    [Fact]
    public async Task Commands_ReturnForbidden_ForNonAdministrator()
    {
        _harness.CurrentUser.IsClubAdmin = false;
        var service = CreateService();

        var created = await service.CreateAsync(
            new CreateSeasonInput
            {
                OperationId = Guid.NewGuid(),
                Name = "Forbidden",
                StartDate = new DateOnly(2027, 1, 1)
            },
            TestContext.Current.CancellationToken);
        var updated = await service.UpdateAsync(
            CurrentSeasonId,
            new UpdateSeasonInput
            {
                ExpectedConcurrencyToken = Guid.NewGuid(),
                Name = "Forbidden",
                StartDate = new DateOnly(2026, 1, 1)
            },
            TestContext.Current.CancellationToken);
        var advanced = await service.StartNextAsync(
            new StartNextSeasonInput
            {
                OperationId = Guid.NewGuid(),
                ExpectedCurrentSeasonId = CurrentSeasonId,
                Name = "Forbidden",
                StartDate = new DateOnly(2027, 1, 1)
            },
            TestContext.Current.CancellationToken);

        created.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        updated.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        advanced.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies no-current creation installs one pointer and recovers repeated operations.</summary>
    [Fact]
    public async Task CreateAsync_CreatesCurrentSeason_Idempotently_WhenPointerIsMissing()
    {
        await using (var db = _harness.CreateAdminContext())
        {
            (await db.Clubs.SingleAsync(TestContext.Current.CancellationToken)).CurrentSeasonId = null;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var input = new CreateSeasonInput
        {
            OperationId = Guid.NewGuid(),
            Name = "  Recovery Season  ",
            StartDate = new DateOnly(2027, 1, 1)
        };
        var service = CreateService();
        var first = await service.CreateAsync(input, TestContext.Current.CancellationToken);
        var repeated = await service.CreateAsync(input, TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        first.Value.Name.ShouldBe("Recovery Season");
        repeated.IsSuccess.ShouldBeTrue();
        repeated.Value.ShouldBe(first.Value);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Clubs.SingleAsync(TestContext.Current.CancellationToken))
            .CurrentSeasonId.ShouldBe(first.Value.SeasonId);
        (await verify.Seasons.CountAsync(
            season => season.CreationOperationId == input.OperationId,
            TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>Verifies exact stored names remain unique across every season command.</summary>
    [Fact]
    public async Task Commands_ReturnConflict_ForExactStoredDuplicateNames()
    {
        Guid currentToken;
        await using (var db = _harness.CreateAdminContext())
        {
            currentToken = await db.Seasons
                .Where(season => season.SeasonId == CurrentSeasonId)
                .Select(season => season.ConcurrencyToken)
                .SingleAsync(TestContext.Current.CancellationToken);
            db.Seasons.Add(new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = "Duplicate",
                StartDate = new DateOnly(2025, 1, 1),
                ClubId = ClubId,
                CreatedById = AdminId
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();
        var updated = await service.UpdateAsync(
            CurrentSeasonId,
            new UpdateSeasonInput
            {
                ExpectedConcurrencyToken = currentToken,
                Name = "Duplicate",
                StartDate = new DateOnly(2026, 1, 1)
            },
            TestContext.Current.CancellationToken);
        var advanced = await service.StartNextAsync(
            new StartNextSeasonInput
            {
                OperationId = Guid.NewGuid(),
                ExpectedCurrentSeasonId = CurrentSeasonId,
                Name = "Duplicate",
                StartDate = new DateOnly(2027, 1, 1)
            },
            TestContext.Current.CancellationToken);

        updated.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        advanced.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Clubs.SingleAsync(TestContext.Current.CancellationToken))
            .CurrentSeasonId.ShouldBe(CurrentSeasonId);
        (await verify.Seasons.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    /// <summary>Verifies metadata writes rotate their token and do not change currentness.</summary>
    [Fact]
    public async Task UpdateAsync_RotatesToken_AndPreservesCurrentPointer()
    {
        Guid token;
        await using (var read = _harness.CreateAdminContext())
        {
            token = await read.Seasons
                .Where(season => season.SeasonId == CurrentSeasonId)
                .Select(season => season.ConcurrencyToken)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().UpdateAsync(
            CurrentSeasonId,
            new UpdateSeasonInput
            {
                ExpectedConcurrencyToken = token,
                Name = "  Renamed  ",
                StartDate = new DateOnly(2026, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Renamed");
        result.Value.ConcurrencyToken.ShouldNotBe(token);
        result.Value.IsCurrent.ShouldBeTrue();
        await using var verify = _harness.CreateAdminContext();
        (await verify.Clubs.SingleAsync(TestContext.Current.CancellationToken))
            .CurrentSeasonId.ShouldBe(CurrentSeasonId);
    }

    /// <summary>Verifies stale metadata writes fail deterministically.</summary>
    [Fact]
    public async Task UpdateAsync_ReturnsConflict_ForStaleToken()
    {
        var result = await CreateService().UpdateAsync(
            CurrentSeasonId,
            new UpdateSeasonInput
            {
                ExpectedConcurrencyToken = Guid.NewGuid(),
                Name = "Renamed",
                StartDate = new DateOnly(2026, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>Verifies finite season edits cannot strand a linked open-ended campaign.</summary>
    [Fact]
    public async Task UpdateAsync_ReturnsValidation_WhenCampaignFallsOutsideWindow()
    {
        var token = await SeedClosedCampaignAsync(new DateOnly(2026, 2, 1), endDate: null);

        var result = await CreateService().UpdateAsync(
            CurrentSeasonId,
            new UpdateSeasonInput
            {
                ExpectedConcurrencyToken = token,
                Name = "Changed",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 12, 31)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.Count.ShouldBe(1);
        result.Problem.Errors.ShouldContainKey(nameof(UpdateSeasonInput.EndDate));
        result.Problem.Errors.ShouldNotContainKey(nameof(UpdateSeasonInput.StartDate));
        await using var verify = _harness.CreateAdminContext();
        var unchanged = await verify.Seasons.SingleAsync(
            season => season.SeasonId == CurrentSeasonId,
            TestContext.Current.CancellationToken);
        unchanged.Name.ShouldBe("Current");
        unchanged.ConcurrencyToken.ShouldBe(token);
    }

    /// <summary>Verifies a lower-bound campaign-window failure targets the season start date.</summary>
    [Fact]
    public async Task UpdateAsync_ReturnsStartDateValidation_WhenCampaignStartsBeforeWindow()
    {
        var token = await SeedClosedCampaignAsync(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 4, 1));

        var result = await CreateService().UpdateAsync(
            CurrentSeasonId,
            new UpdateSeasonInput
            {
                ExpectedConcurrencyToken = token,
                Name = "Changed",
                StartDate = new DateOnly(2026, 3, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.Count.ShouldBe(1);
        result.Problem.Errors.ShouldContainKey(nameof(UpdateSeasonInput.StartDate));
        result.Problem.Errors.ShouldNotContainKey(nameof(UpdateSeasonInput.EndDate));
    }

    /// <summary>Verifies independent lower and upper campaign-window failures target both date fields.</summary>
    [Fact]
    public async Task UpdateAsync_ReturnsBothDateValidations_WhenCampaignCrossesWindow()
    {
        var token = await SeedClosedCampaignAsync(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 10, 1));

        var result = await CreateService().UpdateAsync(
            CurrentSeasonId,
            new UpdateSeasonInput
            {
                ExpectedConcurrencyToken = token,
                Name = "Changed",
                StartDate = new DateOnly(2026, 3, 1),
                EndDate = new DateOnly(2026, 9, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.Count.ShouldBe(2);
        result.Problem.Errors.ShouldContainKey(nameof(UpdateSeasonInput.StartDate));
        result.Problem.Errors.ShouldContainKey(nameof(UpdateSeasonInput.EndDate));
    }

    /// <summary>Verifies stale expected currentness cannot advance a club.</summary>
    [Fact]
    public async Task StartNextAsync_ReturnsConflict_WhenExpectedCurrentIsStale()
    {
        var result = await CreateService().StartNextAsync(
            new StartNextSeasonInput
            {
                OperationId = Guid.NewGuid(),
                ExpectedCurrentSeasonId = CurrentSeasonId + 1,
                Name = "Next",
                StartDate = new DateOnly(2027, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Clubs.SingleAsync(TestContext.Current.CancellationToken))
            .CurrentSeasonId.ShouldBe(CurrentSeasonId);
    }

    /// <summary>Verifies advancement cannot bootstrap a club from the no-current recovery state.</summary>
    [Fact]
    public async Task StartNextAsync_ReturnsConflict_WhenCurrentSeasonIsMissing()
    {
        await using (var db = _harness.CreateAdminContext())
        {
            (await db.Clubs.SingleAsync(TestContext.Current.CancellationToken)).CurrentSeasonId = null;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().StartNextAsync(
            new StartNextSeasonInput
            {
                OperationId = Guid.NewGuid(),
                ExpectedCurrentSeasonId = CurrentSeasonId,
                Name = "Next",
                StartDate = new DateOnly(2027, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Seasons.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>Verifies a first-season operation identifier cannot masquerade as advancement.</summary>
    [Fact]
    public async Task StartNextAsync_ReturnsConflict_WhenCurrentSeasonCreationOperationIsReused()
    {
        Guid currentSeasonOperationId;
        await using (var db = _harness.CreateAdminContext())
        {
            currentSeasonOperationId = await db.Seasons
                .Where(season => season.SeasonId == CurrentSeasonId)
                .Select(season => season.CreationOperationId)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().StartNextAsync(
            new StartNextSeasonInput
            {
                OperationId = currentSeasonOperationId,
                ExpectedCurrentSeasonId = CurrentSeasonId,
                Name = "Next",
                StartDate = new DateOnly(2027, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Seasons.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.Clubs.SingleAsync(TestContext.Current.CancellationToken))
            .CurrentSeasonId.ShouldBe(CurrentSeasonId);
    }

    /// <summary>Verifies an advancement operation cannot be replayed using its new current season as the expected predecessor.</summary>
    [Fact]
    public async Task StartNextAsync_ReturnsConflict_WhenOperationIsReusedWithNewCurrentSeason()
    {
        var operationId = Guid.NewGuid();
        var service = CreateService();
        var first = await service.StartNextAsync(
            new StartNextSeasonInput
            {
                OperationId = operationId,
                ExpectedCurrentSeasonId = CurrentSeasonId,
                Name = "Next",
                StartDate = new DateOnly(2027, 1, 1)
            },
            TestContext.Current.CancellationToken);
        first.IsSuccess.ShouldBeTrue();

        var collision = await service.StartNextAsync(
            new StartNextSeasonInput
            {
                OperationId = operationId,
                ExpectedCurrentSeasonId = first.Value.CurrentSeason.SeasonId,
                Name = "Another",
                StartDate = new DateOnly(2028, 1, 1)
            },
            TestContext.Current.CancellationToken);

        collision.IsProblem.ShouldBeTrue();
        collision.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Seasons.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
        (await verify.Clubs.SingleAsync(TestContext.Current.CancellationToken))
            .CurrentSeasonId.ShouldBe(first.Value.CurrentSeason.SeasonId);
    }

    /// <summary>Verifies an open campaign blocks advancement without mutating the pointer.</summary>
    [Fact]
    public async Task StartNextAsync_ReturnsConflict_WhenCurrentCampaignIsOpen()
    {
        await using (var db = _harness.CreateAdminContext())
        {
            db.Campaigns.Add(new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = "Open",
                StartDate = new DateOnly(2026, 2, 1),
                Status = CampaignStatus.Active,
                SeasonId = CurrentSeasonId,
                ClubId = ClubId,
                CreatedById = AdminId
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().StartNextAsync(
            new StartNextSeasonInput
            {
                OperationId = Guid.NewGuid(),
                ExpectedCurrentSeasonId = CurrentSeasonId,
                Name = "Next",
                StartDate = new DateOnly(2026, 6, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Clubs.SingleAsync(TestContext.Current.CancellationToken))
            .CurrentSeasonId.ShouldBe(CurrentSeasonId);
    }

    /// <summary>Verifies advancement changes only the club pointer and inserts an empty season.</summary>
    [Fact]
    public async Task StartNextAsync_AdvancesIdempotently_WithoutCopyingDurableState()
    {
        long campaignId;
        long teamId;
        long assignmentId;
        Guid assignmentToken;
        await using (var db = _harness.CreateAdminContext())
        {
            var campaign = new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = "Closed",
                StartDate = new DateOnly(2026, 2, 1),
                Status = CampaignStatus.Closed,
                ClosedAt = DateTimeOffset.UtcNow,
                ClosedById = AdminId,
                SeasonId = CurrentSeasonId,
                ClubId = ClubId,
                CreatedById = AdminId
            };
            var team = new TeamEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = "Durable Team",
                GraduationYear = 2030,
                ClubId = ClubId,
                CreatedById = AdminId
            };
            var player = new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                FirstName = "Durable",
                LastName = "Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubId,
                CreatedById = AdminId
            };
            db.AddRange(campaign, team, player);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            var assignment = new PlayerCampaignAssignmentEntity
            {
                PlayerId = player.PlayerId,
                CampaignId = campaign.CampaignId,
                TryoutNumber = 42,
                PlacementOutcome = PlacementOutcome.Assigned,
                TeamId = team.TeamId,
                ClubId = ClubId,
                CreatedById = AdminId
            };
            db.PlayerCampaignAssignments.Add(assignment);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            campaignId = campaign.CampaignId;
            teamId = team.TeamId;
            assignmentId = assignment.PlayerCampaignAssignmentId;
            assignmentToken = assignment.ConcurrencyToken;
        }

        var input = new StartNextSeasonInput
        {
            OperationId = Guid.NewGuid(),
            ExpectedCurrentSeasonId = CurrentSeasonId,
            Name = "Next",
            StartDate = new DateOnly(2025, 12, 1)
        };
        var service = CreateService();
        var first = await service.StartNextAsync(input, TestContext.Current.CancellationToken);
        var repeated = await service.StartNextAsync(input, TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        repeated.IsSuccess.ShouldBeTrue();
        repeated.Value.ShouldBe(first.Value);
        first.Value.PreviousSeasonId.ShouldBe(CurrentSeasonId);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Clubs.SingleAsync(TestContext.Current.CancellationToken))
            .CurrentSeasonId.ShouldBe(first.Value.CurrentSeason.SeasonId);
        (await verify.Campaigns.SingleAsync(
            campaign => campaign.CampaignId == campaignId,
            TestContext.Current.CancellationToken)).SeasonId.ShouldBe(CurrentSeasonId);
        (await verify.Teams.SingleAsync(
            team => team.TeamId == teamId,
            TestContext.Current.CancellationToken)).Name.ShouldBe("Durable Team");
        var preservedAssignment = await verify.PlayerCampaignAssignments.SingleAsync(
            assignment => assignment.PlayerCampaignAssignmentId == assignmentId,
            TestContext.Current.CancellationToken);
        preservedAssignment.CampaignId.ShouldBe(campaignId);
        preservedAssignment.TeamId.ShouldBe(teamId);
        preservedAssignment.TryoutNumber.ShouldBe(42);
        preservedAssignment.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        preservedAssignment.ConcurrencyToken.ShouldBe(assignmentToken);
        (await verify.Campaigns.CountAsync(
            campaign => campaign.SeasonId == first.Value.CurrentSeason.SeasonId,
            TestContext.Current.CancellationToken)).ShouldBe(0);
        (await verify.PlayerCampaignAssignments.CountAsync(
            assignment => assignment.Campaign.SeasonId == first.Value.CurrentSeason.SeasonId,
            TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    /// <summary>Seeds one linked Closed campaign and returns the current season concurrency token.</summary>
    /// <param name="startDate">The campaign start date.</param>
    /// <param name="endDate">The optional campaign end date.</param>
    /// <returns>The concurrency token required for the metadata update under test.</returns>
    private async Task<Guid> SeedClosedCampaignAsync(DateOnly startDate, DateOnly? endDate)
    {
        await using var db = _harness.CreateAdminContext();
        var token = await db.Seasons
            .Where(season => season.SeasonId == CurrentSeasonId)
            .Select(season => season.ConcurrencyToken)
            .SingleAsync(TestContext.Current.CancellationToken);
        db.Campaigns.Add(new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Closed linked campaign",
            StartDate = startDate,
            EndDate = endDate,
            Status = CampaignStatus.Closed,
            ClosedAt = DateTimeOffset.UtcNow,
            ClosedById = AdminId,
            SeasonId = CurrentSeasonId,
            ClubId = ClubId,
            CreatedById = AdminId
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return token;
    }

    /// <summary>Creates the command service against fresh tenant contexts.</summary>
    private SeasonCommandService CreateService()
        => new(
            new TestDbContextFactory<NovaDbContext>(_harness.CreateTenantContext),
            _harness.CurrentUser);
}
