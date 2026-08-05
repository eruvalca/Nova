using System.Net.Http.Json;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly client implementation of <see cref="IPlayerService"/> that calls player-roster APIs.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpPlayerService(HttpClient http) : IPlayerService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResult<PlayerListItem>>> GetPlayerRosterAsync(
        GetPlayerRosterInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        var url = GetPlayerRosterEndpoints.GetRosterUrl(
            input.ClubId,
            input.Search,
            input.LifecycleStatus,
            input.GraduationYear,
            input.PlayerTagId,
            input.SortBy,
            input.SortDirection,
            input.Page,
            input.PageSize);

        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<PagedResult<PlayerListItem>>(
            "The server returned an invalid player roster response.",
            IsValidRoster,
            cancellationToken);
    }

    /// <summary>
    /// Validates the portable invariants of a paged player-roster payload.
    /// </summary>
    /// <param name="roster">The roster to validate.</param>
    /// <returns><see langword="true"/> when the roster is structurally valid.</returns>
    /// <remarks>
    /// The total and page are separate reads, so concurrent changes can make the total lag the rows.
    /// </remarks>
    private static bool IsValidRoster(PagedResult<PlayerListItem> roster)
        => roster.Items is not null
            && roster.Page > 0
            && roster.PageSize > 0
            && roster.PageSize <= GetPlayerRosterInput.MaxPageSize
            && roster.TotalCount >= 0
            && roster.Items.Count <= roster.PageSize
            && roster.Items.All(IsValidPlayer);

    /// <summary>
    /// Validates the portable invariants of a player-roster row.
    /// </summary>
    /// <param name="player">The player row to validate.</param>
    /// <returns><see langword="true"/> when the row is structurally valid.</returns>
    private static bool IsValidPlayer(PlayerListItem player)
        => player is not null
            && player.PlayerId > 0
            && !string.IsNullOrWhiteSpace(player.DisplayName)
            && player.CurrentTags is not null
            && player.ActiveCampaigns is not null
            && player.CurrentTags.All(tag => tag is not null
                && tag.PlayerTagId > 0
                && !string.IsNullOrWhiteSpace(tag.Name)
                && !string.IsNullOrWhiteSpace(tag.Color))
            && player.ActiveCampaigns.All(name => !string.IsNullOrWhiteSpace(name))
            && player.JoinedAt != default;
}
