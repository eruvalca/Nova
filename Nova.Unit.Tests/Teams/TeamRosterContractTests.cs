using Nova.Shared.Teams;
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
        var errors = InputValidator.Validate(new GetTeamRosterInput { LifecycleStatus = " " });

        errors.ShouldContainKey(nameof(GetTeamRosterInput.LifecycleStatus));
    }

    [Fact]
    public void GetRosterUrl_OmitsInvalidOptionalValues()
    {
        var url = TeamRosterEndpoints.GetRosterUrl(" ", "retired", 2200);

        url.ShouldBe("/api/teams");
    }
}
