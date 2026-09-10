namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Requests a page of the campaign-local Closed record, including archived participants.</summary>
public sealed record GetClosedCampaignRosterInput : CampaignRosterDiscoveryInput;
