using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Nova.Extensions.Security;
using Nova.Shared.Security;
using Shouldly;

namespace Nova.Unit.Tests.Security;

/// <summary>
/// Verifies evaluator authorization and the existing Nova policy registrations.
/// </summary>
public sealed class EvaluatorAuthorizationPolicyTests
{
    /// <summary>
    /// Verifies that evaluator authorization requires authentication and approved club membership.
    /// </summary>
    /// <param name="isAuthenticated">Whether the test principal is authenticated.</param>
    /// <param name="hasClub">Whether the test principal carries a club membership claim.</param>
    /// <param name="role">The optional role assigned to the test principal.</param>
    /// <param name="expected">Whether evaluator authorization should succeed.</param>
    [Theory]
    [InlineData(false, false, null, false)]
    [InlineData(true, false, null, false)]
    [InlineData(true, true, null, true)]
    [InlineData(true, true, Roles.ClubAdmin, true)]
    [InlineData(true, false, Roles.Admin, false)]
    [InlineData(true, true, Roles.Admin, true)]
    public async Task EvaluatorPolicy_ReturnsExpectedResult_ForAuthorizationMatrix(
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
            Policies.RequireEvaluator);

        result.Succeeded.ShouldBe(expected);
    }

    /// <summary>
    /// Verifies that evaluator and club-member policy names enforce equivalent requirements.
    /// </summary>
    [Fact]
    public async Task EvaluatorPolicy_HasEquivalentRequirements_ToClubMemberPolicy()
    {
        using var serviceProvider = CreateServiceProvider();
        var policyProvider = serviceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        var clubMemberPolicy = await policyProvider.GetPolicyAsync(Policies.RequireClubMember);
        var evaluatorPolicy = await policyProvider.GetPolicyAsync(Policies.RequireEvaluator);

        clubMemberPolicy.ShouldNotBeNull();
        evaluatorPolicy.ShouldNotBeNull();
        evaluatorPolicy.AuthenticationSchemes.ShouldBe(clubMemberPolicy.AuthenticationSchemes);
        evaluatorPolicy.Requirements.Select(requirement => requirement.GetType())
            .ShouldBe(clubMemberPolicy.Requirements.Select(requirement => requirement.GetType()));

        var clubClaimRequirement = clubMemberPolicy.Requirements
            .OfType<ClaimsAuthorizationRequirement>()
            .ShouldHaveSingleItem();
        var evaluatorClaimRequirement = evaluatorPolicy.Requirements
            .OfType<ClaimsAuthorizationRequirement>()
            .ShouldHaveSingleItem();

        evaluatorClaimRequirement.ClaimType.ShouldBe(clubClaimRequirement.ClaimType);
        evaluatorClaimRequirement.AllowedValues.ShouldBe(clubClaimRequirement.AllowedValues);
    }

    /// <summary>
    /// Verifies that adding the evaluator policy does not change existing authorization behavior.
    /// </summary>
    /// <param name="policyName">The existing policy to authorize against.</param>
    /// <param name="hasClub">Whether the test principal carries a club membership claim.</param>
    /// <param name="role">The optional role assigned to the test principal.</param>
    /// <param name="expected">Whether authorization should succeed.</param>
    [Theory]
    [InlineData(Policies.RequireAdmin, false, Roles.Admin, true)]
    [InlineData(Policies.RequireAdmin, true, Roles.ClubAdmin, false)]
    [InlineData(Policies.RequireClubAdmin, false, Roles.ClubAdmin, true)]
    [InlineData(Policies.RequireClubAdmin, false, Roles.Admin, true)]
    [InlineData(Policies.RequireClubMember, true, Roles.StandardUser, true)]
    [InlineData(Policies.RequireClubMember, false, Roles.Admin, false)]
    public async Task ExistingPolicy_ReturnsExpectedResult_AfterEvaluatorRegistration(
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
