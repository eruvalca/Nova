using Nova.Features.Shared;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Security;

namespace Nova.Features.Dashboard;

/// <summary>
/// Maps the authorized club dashboard summary and recent-activity read endpoints.
/// </summary>
internal static class DashboardEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the club dashboard summary and recent-activity GET endpoints under the shared dashboard group.
        /// </summary>
        /// <returns>The endpoint route builder for chaining.</returns>
        public IEndpointRouteBuilder MapDashboardEndpoints()
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            var group = endpoints
                .MapGroup(DashboardEndpoints.GroupPrefix)
                .RequireAuthorization(Policies.RequireClubMember);

            group.MapGet(DashboardEndpoints.GetSummaryRelative, GetDashboardHandler)
                .Produces<ClubDashboardResult>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(DashboardEndpoints.GetSummaryRouteName);

            group.MapGet(DashboardEndpoints.GetActivityRelative, GetActivityHandler)
                .Produces<DashboardActivityResult>()
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(DashboardEndpoints.GetActivityRouteName);

            group.MapGet(DashboardEndpoints.GetAttentionRelative, GetAttentionHandler)
                .RequireAuthorization(Policies.RequireClubAdmin)
                .Produces<AdminAttentionResult>()
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status403Forbidden)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
                .WithName(DashboardEndpoints.GetAttentionRouteName);

            return endpoints;
        }
    }

    /// <summary>
    /// Handles the club dashboard summary GET request and converts the service result to an HTTP response.
    /// </summary>
    /// <param name="dashboardQueryService">The service that resolves the dashboard summary.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    /// <returns>The HTTP result for the dashboard summary.</returns>
    private static async Task<IResult> GetDashboardHandler(
        IDashboardQueryService dashboardQueryService,
        CancellationToken cancellationToken)
    {
        var result = await dashboardQueryService.GetDashboardAsync(cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Handles the club dashboard recent-activity GET request and converts the service result to an HTTP response.
    /// </summary>
    /// <param name="input">The activity query parameters.</param>
    /// <param name="dashboardQueryService">The service that resolves the activity query.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    /// <returns>The HTTP result for the recent-activity feed.</returns>
    private static async Task<IResult> GetActivityHandler(
        [AsParameters] GetDashboardActivityInput input,
        IDashboardQueryService dashboardQueryService,
        CancellationToken cancellationToken)
    {
        var result = await dashboardQueryService.GetActivityAsync(input, cancellationToken);
        return result.ToHttpResult();
    }

    /// <summary>Handles the administrator attention GET request.</summary>
    /// <param name="attentionQueryService">The service that composes independent attention projections.</param>
    /// <param name="cancellationToken">Propagates notification that the request should be cancelled.</param>
    /// <returns>The HTTP result for administrator attention.</returns>
    private static async Task<IResult> GetAttentionHandler(
        IAdminAttentionQueryService attentionQueryService,
        CancellationToken cancellationToken)
        => (await attentionQueryService.GetAsync(cancellationToken)).ToHttpResult();
}
