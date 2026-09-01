using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;

namespace Nova.Client.Services.Dashboard;

/// <summary>Reads and validates the administrator attention endpoint over HTTP.</summary>
public sealed class HttpAdminAttentionQueryService(HttpClient http) : IAdminAttentionQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<AdminAttentionResult>> GetAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(DashboardEndpoints.GetAttention, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<AdminAttentionResult>(
            "The server returned an invalid dashboard attention response.",
            result => result is not null && IsValid(result.PendingJoinRequests) && IsValid(result.NeedsPlacement),
            cancellationToken);
    }

    /// <summary>Validates the pending-request projection state and optional values.</summary>
    private static bool IsValid(PendingJoinRequestAttentionDto projection)
        => projection.State == AttentionProjectionState.Unavailable
            ? projection.Count is null && projection.OldestSubmittedAt is null
            : projection.Count is >= 0 && (projection.Count == 0 ? projection.OldestSubmittedAt is null : projection.OldestSubmittedAt is not null);

    /// <summary>Validates the placement projection state and optional single-campaign link.</summary>
    private static bool IsValid(NeedsPlacementAttentionDto projection)
        => projection.State == AttentionProjectionState.Unavailable
            ? projection.Count is null && projection.CampaignId is null && projection.CampaignName is null
            : projection.Count is >= 0
                && (projection.Count == 0
                    ? projection.CampaignId is null && projection.CampaignName is null
                    : (projection.CampaignId is null && projection.CampaignName is null)
                        || (projection.CampaignId is > 0 && !string.IsNullOrWhiteSpace(projection.CampaignName)));
}
