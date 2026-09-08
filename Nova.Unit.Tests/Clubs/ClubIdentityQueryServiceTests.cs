using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.SharedKernel.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

public sealed class ClubIdentityQueryServiceTests : IDisposable
{
    private readonly TenancyTestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCurrentAsyncReturnsTenantIdentityForMemberAndAdministratorAsync(bool isAdministrator)
    {
        Seed();
        _harness.CurrentUser.UserId = 10;
        _harness.CurrentUser.ClubId = 1;
        _harness.CurrentUser.IsClubAdmin = isAdministrator;

        var result = await CreateService().GetCurrentAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new()
        {
            ClubId = 1,
            Name = "North Star Volleyball Club",
            City = "Duluth",
            State = "MN",
            HasCrest = true
        });
        typeof(Nova.SharedKernel.Features.Clubs.ClubIdentityResult).GetProperties()
            .Select(property => property.Name)
            .ShouldBe(["ClubId", "Name", "City", "State", "HasCrest"]);
    }

    [Theory]
    [InlineData(null, 1L)]
    [InlineData(10L, null)]
    public async Task GetCurrentAsyncReturnsForbiddenWithoutCompleteMembershipAsync(long? userId, long? clubId)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;

        var result = await CreateService().GetCurrentAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetCurrentAsyncReturnsNotFoundWhenClubDoesNotExistAsync()
    {
        _harness.CurrentUser.UserId = 10;
        _harness.CurrentUser.ClubId = 999;

        var result = await CreateService().GetCurrentAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        result.Problem.Detail.ShouldBe("The current club was not found.");
    }

    [Fact]
    public async Task GetCurrentAsyncReturnsServerErrorWhenReadFailsAsync()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<NovaReadDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns<Task<NovaReadDbContext>>(_ => throw new InvalidOperationException("boom"));
        var service = new ClubIdentityQueryService(
            throwingFactory,
            _harness.CurrentUser,
            NullLogger<ClubIdentityQueryService>.Instance);

        _harness.CurrentUser.UserId = 10;
        _harness.CurrentUser.ClubId = 1;

        var result = await service.GetCurrentAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    private ClubIdentityQueryService CreateService()
    {
        IDbContextFactory<NovaReadDbContext> factory = new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext);
        return new(factory, _harness.CurrentUser, NullLogger<ClubIdentityQueryService>.Instance);
    }

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity { ClubId = 1, CreationOperationId = Guid.NewGuid(), Name = "North Star Volleyball Club", City = "Duluth", State = "MN", CreatedById = 10 },
            new ClubEntity { ClubId = 2, CreationOperationId = Guid.NewGuid(), Name = "Other Club", City = "Madison", State = "WI", CreatedById = 20 });
        db.ClubCrests.Add(new ClubCrestEntity { ClubCrestId = 50, ClubId = 1, OriginalBlobName = "crest-original", CreatedById = 10 });
        db.SaveChanges();
    }
}
