using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.Extensions.Security;
using Nova.Shared.Security;
using Shouldly;

namespace Nova.Unit.Tests.Security;

/// <summary>
/// Verifies the Nova authorization policy registrations and their authorization matrices.
/// </summary>
public sealed class AuthorizationPolicyTests
{
    /// <summary>
    /// Verifies that club-member authorization requires authentication and a club membership claim.
    /// </summary>
    /// <param name="isAuthenticated">Whether the test principal is authenticated.</param>
    /// <param name="hasClub">Whether the test principal carries a club membership claim.</param>
    /// <param name="role">The optional role assigned to the test principal.</param>
    /// <param name="expected">Whether club-member authorization should succeed.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false, null, false)]
    [InlineData(false, true, null, false)]
    [InlineData(true, false, null, false)]
    [InlineData(true, true, null, true)]
    [InlineData(true, true, Roles.ClubAdmin, true)]
    [InlineData(true, false, Roles.Admin, false)]
    [InlineData(true, true, Roles.Admin, true)]
    public async Task ClubMemberPolicy_ReturnsExpectedResult_ForAuthorizationMatrix(
        bool isAuthenticated,
        bool hasClub,
        string? role,
        bool expected)
    {
        using var serviceProvider = CreateServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
        var principal = CreatePrincipal(isAuthenticated, hasClub, role);

        var result = await authorizationService.AuthorizeAsync(
            principal,
            resource: null,
            Policies.RequireClubMember);

        result.Succeeded.ShouldBe(expected);
    }

    /// <summary>
    /// Verifies the role and club-membership policies produce the expected authorization result.
    /// </summary>
    /// <param name="policyName">The policy to authorize against.</param>
    /// <param name="hasClub">Whether the test principal carries a club membership claim.</param>
    /// <param name="role">The optional role assigned to the test principal.</param>
    /// <param name="expected">Whether authorization should succeed.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(Policies.RequireAdmin, false, Roles.Admin, true)]
    [InlineData(Policies.RequireAdmin, true, Roles.ClubAdmin, false)]
    [InlineData(Policies.RequireClubAdmin, false, Roles.ClubAdmin, true)]
    [InlineData(Policies.RequireClubAdmin, false, Roles.Admin, false)]
    [InlineData(Policies.RequireClubAdmin, true, Roles.Admin, false)]
    [InlineData(Policies.RequireClubMember, true, Roles.StandardUser, true)]
    [InlineData(Policies.RequireClubMember, false, Roles.Admin, false)]
    public async Task ExistingPolicy_ReturnsExpectedResult_AfterNovaPolicyRegistration(
        string policyName,
        bool hasClub,
        string? role,
        bool expected)
    {
        using var serviceProvider = CreateServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
        var principal = CreatePrincipal(isAuthenticated: true, hasClub, role);

        var result = await authorizationService.AuthorizeAsync(
            principal,
            resource: null,
            policyName);

        result.Succeeded.ShouldBe(expected);
    }

    /// <summary>
    /// Verifies the global Admin role is never treated as a club operator. Admin is a platform
    /// role with no club tenancy, so it must fail club-administration authorization even when the
    /// principal happens to carry a club membership claim.
    /// </summary>
    /// <param name="hasClub">Whether the global administrator also carries a club claim.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClubAdminPolicy_DeniesGlobalAdmin(bool hasClub)
    {
        using var serviceProvider = CreateServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
        var principal = CreatePrincipal(isAuthenticated: true, hasClub, Roles.Admin);

        var result = await authorizationService.AuthorizeAsync(
            principal,
            resource: null,
            Policies.RequireClubAdmin);

        result.Succeeded.ShouldBeFalse();
    }

    /// <summary>
    /// Creates a service provider containing the production Nova policy registrations.
    /// </summary>
    /// <returns>A service provider configured for authorization.</returns>
    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationBuilder()
            .AddNovaAuthorizationPolicies();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates a principal for one authorization matrix case.
    /// </summary>
    /// <param name="isAuthenticated">Whether the principal is authenticated.</param>
    /// <param name="hasClub">Whether the principal carries a club membership claim.</param>
    /// <param name="role">The optional role assigned to the principal.</param>
    /// <returns>The configured claims principal.</returns>
    private static ClaimsPrincipal CreatePrincipal(bool isAuthenticated, bool hasClub, string? role)
    {
        List<Claim> claims = [];
        if (hasClub)
        {
            claims.Add(new Claim(NovaClaimTypes.ClubId, "7"));
        }

        if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(
            claims,
            isAuthenticated ? "Test" : null,
            ClaimTypes.Name,
            ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }
}
