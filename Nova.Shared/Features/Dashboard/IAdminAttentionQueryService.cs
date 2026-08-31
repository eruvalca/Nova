using Nova.Shared.Results;

namespace Nova.Shared.Features.Dashboard;

/// <summary>Provides administrator-only, tenant-safe attention projections.</summary>
public interface IAdminAttentionQueryService
{
    /// <summary>Gets pending-request and Needs-placement projections independently.</summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The attention projections or an authorization/service problem.</returns>
    Task<ServiceResult<AdminAttentionResult>> GetAsync(CancellationToken cancellationToken = default);
}
