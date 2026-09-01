using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Teams;
using Nova.Shared.Features.Teams;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Features.Teams;

/// <summary>
/// Tests for <see cref="TeamRosterQueryService"/> search filtering using the SQLite tenancy harness.
/// Wildcard characters (<c>%</c>, <c>_</c>) in the search term must be treated as literals,
/// not as SQL LIKE pattern metacharacters.
/// </summary>
public sealed class TeamRosterQueryServiceTests : IDisposable
{
    private const long ClubId = 5_000;
    private const long AdminId = 5_001;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes the test class by seeding the SQLite harness with a fixed team roster.
    /// </summary>
    public TeamRosterQueryServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies that searching for <c>50%</c> returns the one team whose name literally
    /// contains that substring and does not return unrelated names.
    /// </summary>
    [Fact]
    public async Task GetRosterAsync_Search_MatchesLiteralPercent()
    {
        ActAs(AdminId, ClubId);
        var result = await CreateService().GetRosterAsync(
            new GetTeamRosterInput { Search = "50%" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        result.Value[0].Name.ShouldBe("50% Wins");
    }

    /// <summary>
    /// Verifies that searching for <c>50%</c> does not match teams whose names do not
    /// literally contain <c>50%</c>.
    /// </summary>
    [Fact]
    public async Task GetRosterAsync_Search_PercentDoesNotMatchUnrelatedNames()
    {
        ActAs(AdminId, ClubId);
        var result = await CreateService().GetRosterAsync(
            new GetTeamRosterInput { Search = "50%" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotContain(item => item.Name == "U16 Blue");
        result.Value.ShouldNotContain(item => item.Name == "a_b Team");
    }

    /// <summary>
    /// Verifies that searching for <c>a_b</c> does not match a name where <c>_</c>
    /// would otherwise act as a single-character wildcard (e.g. <c>axb</c>).
    /// </summary>
    [Fact]
    public async Task GetRosterAsync_Search_UnderscoreDoesNotMatchSingleCharWildcard()
    {
        ActAs(AdminId, ClubId);
        var result = await CreateService().GetRosterAsync(
            new GetTeamRosterInput { Search = "a_b" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotContain(item => item.Name == "axb Team");
    }

    /// <summary>
    /// Verifies that a bounded limit returns exactly the first rows in deterministic
    /// (Name, then TeamId) order without applying the limit to an unordered set.
    /// </summary>
    [Fact]
    public async Task GetRosterAsync_Limit_ReturnsFirstTeamsInDeterministicOrder()
    {
        ActAs(AdminId, ClubId);
        var result = await CreateService().GetRosterAsync(
            new GetTeamRosterInput { Limit = 2 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(team => team.Name).ShouldBe(["50% Wins", "U16 Blue"]);
    }

    /// <summary>
    /// Verifies that omitting the limit keeps the existing unbounded behavior.
    /// </summary>
    [Fact]
    public async Task GetRosterAsync_WithoutLimit_ReturnsEveryMatchingTeam()
    {
        ActAs(AdminId, ClubId);
        var result = await CreateService().GetRosterAsync(
            new GetTeamRosterInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(4);
    }

    /// <summary>
    /// Verifies a limit that exceeds the match count returns every matching team.
    /// </summary>
    [Fact]
    public async Task GetRosterAsync_LimitAboveMatchCount_ReturnsEveryMatchingTeam()
    {
        ActAs(AdminId, ClubId);
        var result = await CreateService().GetRosterAsync(
            new GetTeamRosterInput { Limit = 200 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(4);
    }

    /// <summary>
    /// Creates a <see cref="TeamRosterQueryService"/> wired to the SQLite tenancy harness.
    /// </summary>
    /// <returns>The configured service under test.</returns>
    private TeamRosterQueryService CreateService()
    {
        IDbContextFactory<NovaReadDbContext> readDbFactory =
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext);
        return new TeamRosterQueryService(
            readDbFactory,
            _harness.CurrentUser,
            NullLogger<TeamRosterQueryService>.Instance);
    }

    /// <summary>
    /// Sets the simulated current user for the tenancy harness.
    /// </summary>
    /// <param name="userId">The simulated user identifier.</param>
    /// <param name="clubId">The simulated club identifier.</param>
    private void ActAs(long? userId, long? clubId)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
    }

    /// <summary>
    /// Seeds the SQLite harness with a club, an admin user, and teams covering the
    /// wildcard-escaping test cases.
    /// </summary>
    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.Add(new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            ClubId = ClubId,
            Name = "Roster Test Club",
            City = "Austin",
            State = "TX",
            CreatedById = AdminId
        });
        db.Users.Add(new NovaUserEntity
        {
            Id = AdminId,
            FirstName = "Admin",
            LastName = "User",
            ClubId = ClubId
        });
        db.Teams.AddRange(
            // Literal percent in name — must match "50%" search exactly.
            new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "50% Wins", GraduationYear = 2028, ClubId = ClubId, CreatedById = AdminId },
            // Unrelated name — must not match "50%" search.
            new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "U16 Blue", GraduationYear = 2028, ClubId = ClubId, CreatedById = AdminId },
            // Literal underscore in name — must match "a_b" search literally.
            new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "a_b Team", GraduationYear = 2029, ClubId = ClubId, CreatedById = AdminId },
            // Single-char substitution name — must NOT match "a_b" search.
            new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "axb Team", GraduationYear = 2030, ClubId = ClubId, CreatedById = AdminId });

        db.SaveChanges();
    }
}
