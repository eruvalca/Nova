using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Client.Services.Campaigns;

/// <summary>Reads bounded history through the same contract as server-rendered Place.</summary>
internal sealed class HttpPlacementContextQueryService(HttpClient http) : IPlacementContextQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlacementContextResult>> GetContextAsync(GetPlacementContextInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0) { return ServiceProblem.Validation(errors); }
        using var response = await http.GetAsync(PlacementContextEndpoints.Url(input), cancellationToken);
        if (!response.IsSuccessStatusCode) { return await response.ToServiceProblemAsync(cancellationToken); }
        return await response.Content.ReadRequiredJsonAsync<PlacementContextResult>(
            "The server returned invalid placement history.", result => Valid(result, input), cancellationToken);
    }

    private static bool Valid(PlacementContextResult result, GetPlacementContextInput input)
    {
        if (result.PlayerCampaignAssignmentId != input.PlayerCampaignAssignmentId || result.History is null
            || result.History.Count > 20 || result.NextEventId is <= 0) { return false; }
        var boundary = input.BeforeEventId ?? long.MaxValue;
        foreach (var item in result.History)
        {
            if (!PlacementHistoryValidation.IsValid(item) || item.EventId >= boundary) { return false; }
            boundary = item.EventId;
        }
        if (result.NextEventId is long next && (next >= (input.BeforeEventId ?? long.MaxValue)
            || (result.History.Count > 0 && next > boundary))) { return false; }
        return result.PreviousPlacement is not { } previous || (previous.Season is { SeasonId: > 0 }
            && !string.IsNullOrWhiteSpace(previous.Season.Name)
            && previous.Source is { Decision: { Outcome: Nova.SharedKernel.Enums.PlacementOutcome.Assigned } decision }
            && !string.IsNullOrWhiteSpace(previous.Source.CampaignName)
            && (decision.TeamId is > 0 || (decision.TeamId is null && previous.Source.Team is null && !previous.CanKeep))
            && decision.SeasonId == previous.Season.SeasonId && decision.PlayerId > 0
            && decision.PlayerCampaignAssignmentId > 0 && decision.CampaignId > 0 && decision.SeasonOpeningSequence > 0
            && decision.RecordedAt > DateTimeOffset.UnixEpoch && decision.RecordedById > 0
            && !string.IsNullOrWhiteSpace(decision.ActorDisplayName) && decision.ConcurrencyToken != Guid.Empty
            && (!previous.CanKeep || previous.Source.Team is { TeamId: > 0 })
            && (previous.Source.Team is not { } team || (team.TeamId == decision.TeamId
                && !string.IsNullOrWhiteSpace(team.TeamName))));
    }
}
