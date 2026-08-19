using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Nova.Data.Tenancy;
using Nova.Shared.Security;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Security;

/// <summary>
/// Tests for <see cref="CurrentUserProvider.GetCurrentUserState"/> and
/// <see cref="NullCurrentUserProvider"/>.
/// </summary>
public sealed class CurrentUserStateTests
{
    /// <summary>
    /// Builds a <see cref="CurrentUserProvider"/> over an HTTP context carrying the given claims.
    /// </summary>
    /// <param name="claims">The claims for the principal; null for an unauthenticated principal.</param>
    /// <returns>The provider.</returns>
    private static CurrentUserProvider CreateProvider(IEnumerable<Claim>? claims)
    {
        var principal = claims is null
            ? new ClaimsPrincipal(new ClaimsIdentity())
            : new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test", nameType: ClaimTypes.Name, roleType: ClaimTypes.Role));

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = principal });

        var services = Substitute.For<IServiceProvider>();
        return new CurrentUserProvider(accessor, services);
    }

    /// <summary>
    /// No authenticated principal yields <see cref="Anonymous"/>.
    /// </summary>
    [Fact]
    public void GetCurrentUserState_WithoutUser_ReturnsAnonymous()
    {
        var state = CreateProvider(claims: null).GetCurrentUserState();

        state.Value.ShouldBeOfType<Anonymous>();
    }

    /// <summary>
    /// A signed-in user without a club claim yields <see cref="AuthenticatedUser"/>.
    /// </summary>
    [Fact]
    public void GetCurrentUserState_WithUserButNoClub_ReturnsAuthenticatedUser()
    {
        var provider = CreateProvider([new Claim(ClaimTypes.NameIdentifier, "42")]);

        var state = provider.GetCurrentUserState();

        var user = state.Value.ShouldBeOfType<AuthenticatedUser>();
        user.UserId.ShouldBe(42);
    }

    /// <summary>
    /// A user with a club claim yields <see cref="ClubMember"/> with role evaluated. The global
    /// Admin role carries no club tenancy, so it must not grant club administration.
    /// </summary>
    /// <param name="role">The optional role claim to apply.</param>
    /// <param name="isClubAdmin">Expected administration capability for the supplied role.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, false)]
    [InlineData(Roles.ClubAdmin, true)]
    [InlineData(Roles.Admin, false)]
    [InlineData(Roles.StandardUser, false)]
    public void GetCurrentUserState_WithClub_ReturnsClubMember(string? role, bool isClubAdmin)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, "42"),
            new(NovaClaimTypes.ClubId, "7"),
        ];
        if (!string.IsNullOrWhiteSpace(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var state = CreateProvider(claims).GetCurrentUserState();

        var member = state.Value.ShouldBeOfType<ClubMember>();
        member.UserId.ShouldBe(42);
        member.ClubId.ShouldBe(7);
        member.IsClubAdmin.ShouldBe(isClubAdmin);
    }

    /// <summary>
    /// Exhaustive Match works over all three cases.
    /// </summary>
    [Fact]
    public void Match_IsExhaustiveOverAllCases()
    {
        var state = CreateProvider([new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(NovaClaimTypes.ClubId, "2")]).GetCurrentUserState();

        var description = state.Match(
            anonymous => "anonymous",
            user => $"user {user.UserId}",
            member => $"member {member.UserId} of club {member.ClubId}");

        description.ShouldBe("member 1 of club 2");
    }

    /// <summary>
    /// The null provider always reports <see cref="Anonymous"/>.
    /// </summary>
    [Fact]
    public void NullCurrentUserProvider_ReturnsAnonymous() => new NullCurrentUserProvider().GetCurrentUserState().Value.ShouldBeOfType<Anonymous>();
}
