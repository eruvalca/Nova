using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>A compact decorative people marker accompanying a complete visible name.</summary>
public partial class CampaignPersonMarker
{
    /// <summary>The full display name shown beside the decorative marker.</summary>
    [Parameter] public string Name { get; set; } = string.Empty;

    private string Initials
    {
        get
        {
            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return string.Empty;
            }
            var first = StringInfo.GetNextTextElement(words[0]);
            return (words.Length == 1 ? first : first + StringInfo.GetNextTextElement(words[^1])).ToUpperInvariant();
        }
    }
}
