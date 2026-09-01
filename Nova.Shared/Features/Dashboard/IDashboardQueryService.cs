using Nova.Shared.Results;

namespace Nova.Shared.Features.Dashboard;

/// <summary>
/// Provides tenant-safe, role-shaped read access to the club dashboard summary. Implemented
/// server-side with direct database access and client-side over HTTP for WebAssembly components.
/// </summary>
public interface IDashboardQueryService
{
    /// <summary>
    /// Retrieves the role-shaped club dashboard summary: active campaign cards and active/archived
    /// roster and team counts.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The dashboard summary or a service problem.</returns>
    Task<ServiceResult<ClubDashboardResult>> GetDashboardAsync(CancellationToken cancellationToken = default);
}
