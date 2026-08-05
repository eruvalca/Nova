using System.Net.Http.Json;
using Nova.Shared.Enums;
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

        var expectedPage = input.Page ?? GetPlayerRosterInput.DefaultPage;
        var expectedPageSize = input.PageSize ?? GetPlayerRosterInput.DefaultPageSize;
        var expectedSortBy = input.SortBy ?? "displayName";
        var expectedSortDirection = input.SortDirection ?? "asc";
        var expectedLifecycleStatus = string.Equals(
            input.LifecycleStatus,
            "archived",
            StringComparison.OrdinalIgnoreCase)
                ? LifecycleStatus.Archived
                : LifecycleStatus.Active;
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
            roster => IsValidRoster(
                roster,
                expectedPage,
                expectedPageSize,
                expectedLifecycleStatus,
                input.GraduationYear,
                input.PlayerTagId,
                expectedSortBy,
                expectedSortDirection),
            cancellationToken);
    }

    /// <summary>
    /// Validates the portable invariants of a paged player-roster payload.
    /// </summary>
    /// <param name="roster">The roster to validate.</param>
    /// <param name="expectedPage">The page requested by the caller.</param>
    /// <param name="expectedPageSize">The page size requested by the caller.</param>
    /// <param name="expectedLifecycleStatus">The lifecycle filter applied by the server.</param>
    /// <param name="expectedGraduationYear">The optional exact graduation-year filter.</param>
    /// <param name="expectedPlayerTagId">The optional active-campaign tag filter.</param>
    /// <param name="expectedSortBy">The effective sort field requested by the caller.</param>
    /// <param name="expectedSortDirection">The effective sort direction requested by the caller.</param>
    /// <returns><see langword="true"/> when the roster is structurally valid.</returns>
    /// <remarks>
    /// The total and page are separate reads, so concurrent changes can make the total lag the rows.
    /// </remarks>
    private static bool IsValidRoster(
        PagedResult<PlayerListItem> roster,
        int expectedPage,
        int expectedPageSize,
        LifecycleStatus expectedLifecycleStatus,
        int? expectedGraduationYear,
        long? expectedPlayerTagId,
        string expectedSortBy,
        string expectedSortDirection)
        => roster.Items is not null
            && roster.Page == expectedPage
            && roster.PageSize == expectedPageSize
            && roster.TotalCount >= 0
            && roster.Items.Count <= roster.PageSize
            && roster.Items.All(player => IsValidPlayer(
                player,
                expectedLifecycleStatus,
                expectedGraduationYear,
                expectedPlayerTagId))
            && ArePlayersOrdered(roster.Items, expectedSortBy, expectedSortDirection);

    /// <summary>
    /// Validates portable roster ordering keys for the requested sort.
    /// </summary>
    /// <param name="players">The roster rows to validate.</param>
    /// <param name="sortBy">The effective sort field.</param>
    /// <param name="sortDirection">The effective sort direction.</param>
    /// <returns><see langword="true"/> when adjacent rows retain the contracted portable order.</returns>
    private static bool ArePlayersOrdered(
        IReadOnlyList<PlayerListItem> players,
        string sortBy,
        string sortDirection)
    {
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return players.Zip(players.Skip(1)).All(pair =>
        {
            if (string.Equals(sortBy, "joinedAt", StringComparison.OrdinalIgnoreCase))
            {
                return descending
                    ? pair.First.JoinedAt > pair.Second.JoinedAt
                        || (pair.First.JoinedAt == pair.Second.JoinedAt
                            && pair.First.PlayerId < pair.Second.PlayerId)
                    : pair.First.JoinedAt < pair.Second.JoinedAt
                        || (pair.First.JoinedAt == pair.Second.JoinedAt
                            && pair.First.PlayerId < pair.Second.PlayerId);
            }

            return true;
        });
    }

    /// <summary>
    /// Validates the portable invariants of a player-roster row.
    /// </summary>
    /// <param name="player">The player row to validate.</param>
    /// <param name="expectedLifecycleStatus">The lifecycle filter applied by the server.</param>
    /// <param name="expectedGraduationYear">The optional exact graduation-year filter.</param>
    /// <param name="expectedPlayerTagId">The optional active-campaign tag filter.</param>
    /// <returns><see langword="true"/> when the row is structurally valid.</returns>
    private static bool IsValidPlayer(
        PlayerListItem player,
        LifecycleStatus expectedLifecycleStatus,
        int? expectedGraduationYear,
        long? expectedPlayerTagId)
        => player is not null
            && player.PlayerId > 0
            && !string.IsNullOrWhiteSpace(player.DisplayName)
            && player.LifecycleStatus == expectedLifecycleStatus
            && (expectedGraduationYear is null
                || player.GraduationYear == expectedGraduationYear)
            && player.CurrentTags is not null
            && player.ActiveCampaigns is not null
            && player.CurrentTags.All(tag => tag is not null
                && tag.PlayerTagId > 0
                && !string.IsNullOrWhiteSpace(tag.Name)
                && !string.IsNullOrWhiteSpace(tag.Color))
            && (expectedPlayerTagId is null
                || player.CurrentTags.Any(tag => tag.PlayerTagId == expectedPlayerTagId))
            && player.ActiveCampaigns.All(name => !string.IsNullOrWhiteSpace(name))
            && player.JoinedAt != default;
}
