using System.Text.Json.Serialization;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>The lifecycle reason a campaign cannot be reopened, independently of the viewer's role.</summary>
public enum CampaignReopenUnavailableReason
{
    None,
    NotClosed,
    HistoricalSeason,
    MissingOpening,
    LaterCampaignOpened,
    AnotherActiveCampaign,
}

/// <summary>Advisory lifecycle actions from current persisted authority and campaign state; commands recheck under locks.</summary>
public sealed record CampaignLifecycleCapabilities(
    [property: JsonRequired] bool IsAdministrator,
    [property: JsonRequired] bool CanClose,
    [property: JsonRequired] bool CanReopen,
    [property: JsonRequired] CampaignReopenUnavailableReason ReopenUnavailableReason,
    [property: JsonRequired] long? RelatedCampaignId);
