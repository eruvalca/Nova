using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Services;

/// <summary>An authorized workspace read that owns one lifecycle review.</summary>
public sealed record CampaignLifecycleEvidence(
    string Owner, long Generation, CampaignDetailResult Detail, CampaignCloseoutReadinessDto Readiness);
