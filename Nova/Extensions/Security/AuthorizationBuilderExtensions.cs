using Microsoft.AspNetCore.Authorization;
using Nova.Shared.Security;

namespace Nova.Extensions.Security;

/// <summary>
/// Provides Nova authorization policy registration for the server host.
/// </summary>
public static class AuthorizationBuilderExtensions
{
    extension(AuthorizationBuilder builder)
    {
        /// <summary>
        /// Registers the role, club membership, and evaluator policies used by Nova.
        /// </summary>
        /// <returns>The authorization builder for further configuration.</returns>
        public AuthorizationBuilder AddNovaAuthorizationPolicies()
        {
            static void ConfigureClubMemberPolicy(AuthorizationPolicyBuilder policy)
            {
                policy.RequireAuthenticatedUser()
                    .RequireClaim(NovaClaimTypes.ClubId);
            }

            return builder
                .AddPolicy(Policies.RequireAdmin, policy => policy.RequireRole(Roles.Admin))
                .AddPolicy(Policies.RequireClubAdmin, policy => policy.RequireRole(Roles.ClubAdmin, Roles.Admin))
                .AddPolicy(Policies.RequireClubMember, ConfigureClubMemberPolicy)
                .AddPolicy(Policies.RequireEvaluator, ConfigureClubMemberPolicy);
        }
    }
}
