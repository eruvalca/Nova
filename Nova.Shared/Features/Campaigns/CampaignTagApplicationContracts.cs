namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Reports the created campaign tag application identifier.
/// </summary>
/// <param name="CampaignTagApplicationId">The created campaign tag application identifier.</param>
public readonly record struct CampaignTagApplicationMutationSuccess(long CampaignTagApplicationId);
