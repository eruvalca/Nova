using Nova.Shared.Results;

namespace Nova.Shared.Features.Dashboard;

/// <summary>
/// Provides tenant-safe, role-shaped read access to the club dashboard summary and its bounded
/// recent-activity feed. Implemented server-side with direct database access and client-side over
/// HTTP for WebAssembly components.
/// </summary>
public interface IDashboardQueryService
{
    /// <summary>
    /// Retrieves the role-shaped club dashboard summary: active campaign cards, active/archived roster
    /// and team counts, and administrator-only attention counts.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The dashboard summary or a service problem.</returns>
    Task<ServiceResult<ClubDashboardResult>> GetDashboardAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the bounded, deterministically ordered recent-activity feed for the caller's club.
    /// </summary>
    /// <param name="input">The optional bound on returned activity events.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The activity feed or a service problem.</returns>
    Task<ServiceResult<DashboardActivityResult>> GetActivityAsync(
        GetDashboardActivityInput input,
        CancellationToken cancellationToken = default);
}
