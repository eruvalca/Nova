using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Verifies team-roster routes and filter validation.
/// </summary>
public sealed class TeamRosterContractTests
{
    [Fact]
    public void GetRosterUrlBuildsExpectedUrl()
    {
        var url = TeamRosterEndpoints.GetRosterUrl(" U16 ", "ARCHIVED", 2032);

        url.ShouldBe("/api/teams?search=U16&lifecycleStatus=archived&graduationYear=2032");
    }

    [Fact]
    public void GetTeamRosterInputDefaultsToActiveWhenStatusIsOmitted()
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput());

        errors.ShouldBeEmpty();
    }

    [Fact]
    public void GetTeamRosterInputRejectsUnsupportedLifecycleStatus()
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput { LifecycleStatus = "retired" });

        errors.ShouldContainKey(nameof(GetTeamRosterInput.LifecycleStatus));
    }

    /// <summary>
    /// Verifies an explicitly blank lifecycle status is not treated as omitted.
    /// </summary>
    [Fact]
    public void GetTeamRosterInputRejectsBlankLifecycleStatus()
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput { LifecycleStatus = string.Empty });

        errors.ShouldContainKey(nameof(GetTeamRosterInput.LifecycleStatus));
    }

    [Fact]
    public void GetRosterUrlOmitsInvalidOptionalValues()
    {
        var url = TeamRosterEndpoints.GetRosterUrl(" ", "retired", 2200);

        url.ShouldBe("/api/teams");
    }

    [Fact]
    public void GetRosterUrlIncludesBoundedLimit()
    {
        var url = TeamRosterEndpoints.GetRosterUrl(limit: 200);

        url.ShouldBe("/api/teams?limit=200");
    }

    [Fact]
    public void GetRosterUrlOmitsOutOfRangeLimit()
    {
        var url = TeamRosterEndpoints.GetRosterUrl(limit: 201);

        url.ShouldBe("/api/teams");
    }

    /// <summary>
    /// Verifies the cutoff filter the placement surface needs is emitted, because a team is compatible with
    /// every player at or above its graduation year.
    /// </summary>
    [Fact]
    public void GetRosterUrlEmitsTheInclusiveMaximumGraduationYear()
    {
        var url = TeamRosterEndpoints.GetRosterUrl(lifecycleStatus: "active", maxGraduationYear: 2032, limit: 200);

        url.ShouldBe("/api/teams?lifecycleStatus=active&limit=200&maxGraduationYear=2032");
    }

    [Fact]
    public void GetRosterUrlOmitsOutOfRangeMaximumGraduationYear()
    {
        var url = TeamRosterEndpoints.GetRosterUrl(maxGraduationYear: 2200);

        url.ShouldBe("/api/teams");
    }

    /// <summary>
    /// Verifies a maximum graduation year outside the documented year range is rejected.
    /// </summary>
    /// <param name="maxGraduationYear">The out-of-range explicit maximum.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1999)]
    [InlineData(2101)]
    public void GetTeamRosterInputRejectsOutOfRangeMaximumGraduationYear(int maxGraduationYear)
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput { MaxGraduationYear = maxGraduationYear });

        errors.ShouldContainKey(nameof(GetTeamRosterInput.MaxGraduationYear));
    }

    /// <summary>
    /// Verifies the exact cohort filter and the cutoff filter may be combined.
    /// </summary>
    [Fact]
    public void GetTeamRosterInputAcceptsAnExactYearWithAMaximumGraduationYear()
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput { GraduationYear = 2030, MaxGraduationYear = 2032 });

        errors.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies explicit limit values outside the documented 1..200 cap are rejected.
    /// </summary>
    /// <param name="limit">The out-of-range explicit limit.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(201)]
    public void GetTeamRosterInputRejectsOutOfRangeLimit(int limit)
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput { Limit = limit });

        errors.ShouldContainKey(nameof(GetTeamRosterInput.Limit));
    }

    /// <summary>
    /// Verifies the documented cap and an omitted limit both validate.
    /// </summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(200)]
    public void GetTeamRosterInputAcceptsValidLimit(int? limit)
    {
        var errors = InputValidator.Validate(new GetTeamRosterInput { Limit = limit });

        errors.ShouldBeEmpty();
    }
}
