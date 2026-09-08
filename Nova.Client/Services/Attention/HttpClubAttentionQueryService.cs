using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Attention;
using Nova.SharedKernel.Results;

namespace Nova.Client.Services.Attention;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="IClubAttentionQueryService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
internal sealed class HttpClubAttentionQueryService(HttpClient http) : IClubAttentionQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubAttentionResult>> GetClubAttentionAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(new Uri(AttentionEndpoints.GetClubAttention, UriKind.RelativeOrAbsolute), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<ClubAttentionResult>(
            "The server returned an invalid club attention response.",
            IsValidAttentionResult,
            cancellationToken);
    }

    /// <summary>
    /// Validates the structural invariants of a club attention success payload.
    /// </summary>
    /// <param name="result">The deserialized attention payload.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidAttentionResult(ClubAttentionResult result)
        => result is not null
            && IsValidJoinRequestsRegion(result.PendingJoinRequests)
            && IsValidNeedsPlacementRegion(result.NeedsPlacement);

    /// <summary>
    /// Validates the pending join requests region: a loaded region carries a non-negative count and
    /// an oldest-request timestamp only when the count is non-zero.
    /// </summary>
    /// <param name="region">The region to validate.</param>
    /// <returns><see langword="true"/> when the region is structurally valid.</returns>
    private static bool IsValidJoinRequestsRegion(PendingJoinRequestsRegion region)
        => region is not null
            && region.Status is AttentionRegionStatus.Loaded or AttentionRegionStatus.Unavailable
            && (region.Status != AttentionRegionStatus.Loaded || region.Count >= 0)
            && (region.Count > 0 ? region.OldestRequestAt is not null : region.OldestRequestAt is null);

    /// <summary>
    /// Validates the needs placement region: a loaded region carries a non-negative count and the
    /// optional campaign identifier and name, present together only when the count is non-zero.
    /// </summary>
    /// <param name="region">The region to validate.</param>
    /// <returns><see langword="true"/> when the region is structurally valid.</returns>
    private static bool IsValidNeedsPlacementRegion(NeedsPlacementRegion region)
        => region is not null
            && region.Status is AttentionRegionStatus.Loaded or AttentionRegionStatus.Unavailable
            && (region.Status != AttentionRegionStatus.Loaded || region.Count >= 0)
            && (region.Count > 0
                ? region.CampaignId is > 0 && !string.IsNullOrWhiteSpace(region.CampaignName)
                : region.CampaignId is null && region.CampaignName is null);
}
