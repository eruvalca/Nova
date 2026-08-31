using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;

namespace Nova.Client.Services.Dashboard;

public sealed class HttpAdminAttentionQueryService(HttpClient http) : IAdminAttentionQueryService
{
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

    private static bool IsValid(PendingJoinRequestAttentionDto projection)
        => projection.State == AttentionProjectionState.Unavailable
            ? projection.Count is null && projection.OldestSubmittedAt is null
            : projection.Count is >= 0 && (projection.Count == 0 ? projection.OldestSubmittedAt is null : projection.OldestSubmittedAt is not null);

    private static bool IsValid(NeedsPlacementAttentionDto projection)
        => projection.State == AttentionProjectionState.Unavailable
            ? projection.Count is null && projection.CampaignId is null
            : projection.Count is >= 0 && (projection.Count == 0 ? projection.CampaignId is null : projection.CampaignId is > 0);
}
