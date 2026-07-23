using Nova.Shared.Features.Players;
using Nova.Shared.Validation;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests route constants, URL builders, and input validation for player-roster contracts.
/// </summary>
public sealed class GetPlayerRosterContractTests
{
    [Fact]
    public void GetRosterTemplate_HasExpectedValue() =>
        GetPlayerRosterEndpoints.GetRosterTemplate.ShouldBe("/api/clubs/{clubId:long}/players/roster");

    [Fact]
    public void GetRosterUrl_BuildsExpectedUrl_WithAllQueryParameters()
    {
        var url = GetPlayerRosterEndpoints.GetRosterUrl(
            clubId: 42,
            search: "Bo B",
            lifecycleStatus: "archived",
            graduationYear: 2031,
            playerTagId: 8,
            sortBy: "joinedAt",
            sortDirection: "desc",
            page: 3,
            pageSize: 50);

        url.ShouldBe("/api/clubs/42/players/roster?search=Bo%20B&lifecycleStatus=archived&graduationYear=2031&playerTagId=8&sortBy=joinedAt&sortDirection=desc&page=3&pageSize=50");
    }

    [Fact]
    public void GetPlayerRosterInput_DefaultsToConfiguredPagingValues()
    {
        var input = new GetPlayerRosterInput { ClubId = 42 };

        input.Page.ShouldBe(GetPlayerRosterInput.DefaultPage);
        input.PageSize.ShouldBe(GetPlayerRosterInput.DefaultPageSize);
    }

    [Fact]
    public void GetPlayerRosterInput_ReturnsValidationError_ForInvalidSortBy()
    {
        var input = new GetPlayerRosterInput { ClubId = 42, SortBy = "height" };

        var errors = InputValidator.Validate(input);

        errors.ShouldContainKey(nameof(GetPlayerRosterInput.SortBy));
    }

    [Fact]
    public void GetPlayerRosterInput_ReturnsValidationError_ForInvalidLifecycleStatus()
    {
        var input = new GetPlayerRosterInput { ClubId = 42, LifecycleStatus = "retired" };

        var errors = InputValidator.Validate(input);

        errors.ShouldContainKey(nameof(GetPlayerRosterInput.LifecycleStatus));
    }
}
