namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>
/// Reports the created campaign tag application identifier.
/// </summary>
/// <param name="CampaignTagApplicationId">The affected application identifier.</param>
/// <param name="PlayerTagId">The resolved definition identifier.</param>
/// <param name="AlreadyApplied">Whether the application already belonged to its original actor.</param>
/// <param name="Receipt">Immutable proof of this operation.</param>
public readonly record struct CampaignTagApplicationMutationSuccess(
    long CampaignTagApplicationId, long PlayerTagId, [property: System.Text.Json.Serialization.JsonRequired] bool AlreadyApplied, EvaluationMutationReceipt Receipt);
