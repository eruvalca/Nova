using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Applies and removes campaign tag applications within the current club tenant.
/// </summary>
public interface ICampaignTagApplicationService
{
    /// <summary>
    /// Applies one active tag definition to one participation in an active campaign.
    /// </summary>
    /// <param name="input">The target participation and tag-definition identifiers.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The created application identifier or a structured service problem.</returns>
    Task<ServiceResult<CampaignTagApplicationMutationSuccess>> ApplyAsync(
        ApplyCampaignTagApplicationInput input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one campaign tag application when authorized by ownership or club-administrator role.
    /// </summary>
    /// <param name="input">The campaign tag application to remove.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A success result or a structured service problem.</returns>
    Task<ServiceResult<Success>> RemoveAsync(
        RemoveCampaignTagApplicationInput input,
        CancellationToken cancellationToken = default);
}
