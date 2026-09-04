using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

public sealed class ClubIdentityQueryServiceTests : IDisposable
{
    private readonly TenancyTestHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCurrentAsync_ReturnsTenantIdentity_ForMemberAndAdministrator(bool isAdministrator)
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
        typeof(Nova.Shared.Features.Clubs.ClubIdentityResult).GetProperties()
            .Select(property => property.Name)
            .ShouldBe(["ClubId", "Name", "City", "State", "HasCrest"]);
    }

    [Theory]
    [InlineData(null, 1L)]
    [InlineData(10L, null)]
    public async Task GetCurrentAsync_ReturnsForbidden_WithoutCompleteMembership(long? userId, long? clubId)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;

        var result = await CreateService().GetCurrentAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    private ClubIdentityQueryService CreateService()
    {
        IDbContextFactory<NovaReadDbContext> factory = new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext);
        return new(factory, _harness.CurrentUser);
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
