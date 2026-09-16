using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Services;

/// <summary>A failed Close read restored only for its original authority and campaign detail.</summary>
public sealed record CampaignCloseReadFailure(string Owner, long Generation, CampaignDetailResult Detail, string Message);
