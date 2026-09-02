using System.Net.Http.Json;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;

namespace Nova.Client.Services.Seasons;

/// <summary>Calls the server's season command endpoints over HTTP.</summary>
/// <param name="http">The application HTTP client.</param>
public sealed class HttpSeasonCommandService(HttpClient http) : ISeasonCommandService
{
    /// <inheritdoc />
    public async Task<ServiceResult<SeasonSummary>> CreateAsync(
        CreateSeasonInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(SeasonEndpoints.GroupPrefix, input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<SeasonSummary>(
            "The server returned an invalid season response.",
            season => IsValidSummary(season) && season.IsCurrent,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SeasonSummary>> UpdateAsync(
        long seasonId,
        UpdateSeasonInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(SeasonEndpoints.Detail(seasonId), input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<SeasonSummary>(
            "The server returned an invalid season response.",
            season => IsValidSummary(season)
                && season.SeasonId == seasonId
                && season.ConcurrencyToken != input.ExpectedConcurrencyToken,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<StartNextSeasonResult>> StartNextAsync(
        StartNextSeasonInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            SeasonEndpoints.StartNext,
            input,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<StartNextSeasonResult>(
            "The server returned an invalid season advancement response.",
            result => result is not null
                && result.PreviousSeasonId == input.ExpectedCurrentSeasonId
                && IsValidSummary(result.CurrentSeason)
                && result.CurrentSeason.SeasonId != result.PreviousSeasonId
                && result.CurrentSeason.IsCurrent,
            cancellationToken);
    }

    /// <summary>Validates portable season success-payload invariants.</summary>
    private static bool IsValidSummary(SeasonSummary season)
        => season is not null
            && season.SeasonId > 0
            && !string.IsNullOrWhiteSpace(season.Name)
            && season.StartDate != default
            && (season.EndDate is null || season.EndDate >= season.StartDate)
            && season.ConcurrencyToken != Guid.Empty;
}
