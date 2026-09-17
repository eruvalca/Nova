using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Shouldly;

namespace Nova.Unit.Tests.Players;

public sealed partial class PlayerServiceTests
{
    [Fact]
    public async Task EmptyClubSummaryReturnsRealZeroCountsAndNoYearsAsync()
    {
        using var db = _harness.CreateAdminContext();
        var empty = new ClubEntity
        {
            ClubId = 102,
            CreationOperationId = Guid.CreateVersion7(),
            Name = "Empty directory",
            City = "Austin",
            State = "TX",
            CreatedById = ClubAUserId
        };
        db.Clubs.Add(empty);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = empty.ClubId;
        var result = await CreateService().GetPlayerDirectorySummaryAsync(new() { ClubId = empty.ClubId }, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.ActiveCount.ShouldBe(0);
        result.Value.ArchivedCount.ShouldBe(0);
        result.Value.GraduationYears.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(2029, 1)]
    [InlineData(2030, 0)]
    public async Task DirectoryCombinesLiteralNameYearAndActiveCampaignTagAsync(int year, int expected)
    {
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        using var db = _harness.CreateAdminContext();
        var tag = await db.PlayerTags.Where(row => row.ClubId == ClubAId && row.Name == "Keeper")
            .Select(row => row.PlayerTagId).SingleAsync(TestContext.Current.CancellationToken);
        var result = await CreateService().GetPlayerRosterAsync(new()
        {
            ClubId = ClubAId,
            Search = "  bRoW  ",
            GraduationYear = year,
            PlayerTagId = tag
        }, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(expected);
        result.Value.Items.Select(row => row.DisplayName).ShouldBe(expected == 1 ? ["Bobby Brown"] : Array.Empty<string>());
    }

    [Fact]
    public async Task SummaryCountsBothViewsAndAllYearsForOrdinaryMemberAsync()
    {
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;
        var result = await CreateService().GetPlayerDirectorySummaryAsync(new() { ClubId = ClubAId }, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.ActiveCount.ShouldBe(3);
        result.Value.ArchivedCount.ShouldBe(1);
        result.Value.GraduationYears.ShouldBe([2027, 2028, 2029, 2030]);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, ClubAId)]
    [InlineData(ClubAUserId, ClubBId)]
    public async Task SummaryRejectsAnonymousAndCrossClubReadsAsync(long? userId, long requestedClub)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = ClubAId;
        var result = await CreateService().GetPlayerDirectorySummaryAsync(new() { ClubId = requestedClub }, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(Nova.SharedKernel.Results.ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task SummaryRejectsInvalidClubBeforeReadingAsync()
    {
        var result = await CreateService().GetPlayerDirectorySummaryAsync(new() { ClubId = 0 }, TestContext.Current.CancellationToken);
        result.Problem.Kind.ShouldBe(Nova.SharedKernel.Results.ServiceProblemKind.Validation);
    }

    [Fact]
    public async Task SummaryDoesNotDeriveYearsFromFirstHundredPlayersAsync()
    {
        using (var db = _harness.CreateAdminContext())
        {
            db.Players.AddRange(Enumerable.Range(0, 110).Select(index => new PlayerEntity
            {
                CreationOperationId = Guid.CreateVersion7(),
                ClubId = ClubAId,
                CreatedById = ClubAUserId,
                FirstName = "Duplicate",
                LastName = "Name",
                DateOfBirth = new DateOnly(2010, 1, 1),
                GraduationYear = index == 109 ? 2100 : 2000
            }));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();
        var summary = await service.GetPlayerDirectorySummaryAsync(new() { ClubId = ClubAId }, TestContext.Current.CancellationToken);
        summary.Value.ActiveCount.ShouldBe(113);
        summary.Value.GraduationYears.ShouldBe([2000, 2027, 2028, 2029, 2030, 2100]);
        var first = await service.GetPlayerRosterAsync(new() { ClubId = ClubAId, Search = "Duplicate", Page = 1 }, TestContext.Current.CancellationToken);
        var second = await service.GetPlayerRosterAsync(new() { ClubId = ClubAId, Search = "Duplicate", Page = 2 }, TestContext.Current.CancellationToken);
        first.Value.Items.Count.ShouldBe(20);
        second.Value.Items.Count.ShouldBe(20);
        first.Value.TotalCount.ShouldBe(110);
        first.Value.Items.Select(row => row.PlayerId).Intersect(second.Value.Items.Select(row => row.PlayerId)).ShouldBeEmpty();
        first.Value.Items[^1].PlayerId.ShouldBeLessThan(second.Value.Items[0].PlayerId);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("displayName")]
    [InlineData("joinedAt")]
    public async Task ExtremePageDoesNotWrapOffsetAsync(string sort)
    {
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        var result = await CreateService().GetPlayerRosterAsync(new() { ClubId = ClubAId, Page = int.MaxValue, PageSize = 100, SortBy = sort }, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Page.ShouldBe(int.MaxValue);
        result.Value.Items.ShouldBeEmpty();
        result.Value.TotalCount.ShouldBe(3);
    }
}
