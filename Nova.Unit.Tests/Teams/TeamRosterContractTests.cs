using Nova.Shared.Features.Teams;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Verifies team-roster routes and filter validation.
/// </summary>
public sealed class TeamRosterContractTests
{
    [Fact]
    public void GetRosterUrl_BuildsExpectedUrl()
    {
        var url = TeamRosterEndpoints.GetRosterUrl(" U16 ", "ARCHIVED", 2032);

        url.ShouldBe("/api/teams?search=U16&lifecycleStatus=archived&graduationYear=2032");
    }

    [Fact]
    public void GetTeamRosterInput_DefaultsToActiveWhenStatusIsOmitted()
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput());

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void GetTeamRosterInput_RejectsUnsupportedLifecycleStatus()
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput { LifecycleStatus = "retired" });

        errors.ShouldContainKey(nameof(GetTeamRosterInput.LifecycleStatus));
    }

    /// <summary>
    /// Verifies an explicitly blank lifecycle status is not treated as omitted.
    /// </summary>
    [Fact]
    public void GetTeamRosterInput_RejectsBlankLifecycleStatus()
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput { LifecycleStatus = string.Empty });

        errors.ShouldContainKey(nameof(GetTeamRosterInput.LifecycleStatus));
    }

    [Fact]
    public void GetRosterUrl_OmitsInvalidOptionalValues()
    {
        var url = TeamRosterEndpoints.GetRosterUrl(" ", "retired", 2200);

        url.ShouldBe("/api/teams");
    }

    /// <summary>
    /// Verifies a valid explicit limit is emitted as a query value.
    /// </summary>
    [Fact]
    public void GetRosterUrl_IncludesValidLimit()
    {
        var url = TeamRosterEndpoints.GetRosterUrl(limit: 200);

        url.ShouldBe("/api/teams?limit=200");
    }

    /// <summary>
    /// Verifies an out-of-contract limit is omitted from the URL.
    /// </summary>
    [Fact]
    public void GetRosterUrl_OmitsInvalidLimit()
    {
        var url = TeamRosterEndpoints.GetRosterUrl(limit: 201);

        url.ShouldBe("/api/teams");
    }

    /// <summary>
    /// Verifies an out-of-range limit is rejected by the shared input validation.
    /// </summary>
    /// <param name="limit">The invalid limit value.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void GetTeamRosterInput_RejectsLimitOutOfRange(int limit)
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput { Limit = limit });

        errors.ShouldContainKey(nameof(GetTeamRosterInput.Limit));
    }

    /// <summary>
    /// Verifies an in-range limit passes the shared input validation.
    /// </summary>
    [Fact]
    public void GetTeamRosterInput_AcceptsLimitWithinRange()
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput { Limit = 200 });

        errors.ShouldBeEmpty();
    }
}
