using System.Net.Http.Json;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;

namespace Nova.Client.Services.Players;

/// <summary>
/// WebAssembly client implementation of <see cref="IPlayerManagementService"/> that calls the
/// server's minimal API endpoints over HTTP.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
internal sealed class HttpPlayerManagementService(HttpClient http) : IPlayerManagementService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlayerCreationCompletion>> CreateAsync(
        CreatePlayerInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(PlayerEndpoints.Create, input, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.ToServiceProblemAsync(cancellationToken);
            return !IsValidCreationProblem(problem, input.OperationId)
                ? ServiceProblem.ServerError("The server returned invalid player creation evidence. Retry the original operation.", problem.Extensions)
                : problem;
        }

        return await response.Content.ReadRequiredJsonAsync<PlayerCreationCompletion>(
            "The server returned invalid player creation evidence. Retry the original operation.",
            completion => IsValidCompletion(completion, input),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PlayerDto>> UpdateAsync(
        UpdatePlayerInput input,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(
            PlayerEndpoints.UpdateUrl(input.PlayerId),
            input,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<PlayerDto>(
            "The server returned an invalid player response.",
            player => IsValidPlayer(player, input.PlayerId),
            cancellationToken);
    }

    /// <summary>Validates field feedback separately from the receipt evidence needed to settle an operation.</summary>
    private static bool IsValidCreationProblem(ServiceProblem problem, Guid operationId)
    {
        if (problem.Kind == ServiceProblemKind.Conflict) { return PlayerCreationProblems.IsValidConflict(problem, operationId); }
        if (problem.Kind != ServiceProblemKind.Validation) { return true; }
        return problem.Errors is { Count: > 0 }
            && problem.Errors.All(error => error.Value is { Length: > 0 }
                && error.Value.All(message => !string.IsNullOrWhiteSpace(message)))
            && (problem.Extensions is null || !problem.Extensions.Keys.Any(key => key is
                PlayerCreationProblems.ReasonExtension or PlayerCreationProblems.NotCommittedExtension or PlayerCreationProblems.DuplicateExtension));
    }

    /// <summary>Requires original operation, profile, deadline and enrollment evidence to agree.</summary>
    private static bool IsValidCompletion(PlayerCreationCompletion completion, CreatePlayerInput input)
        => completion is not null && completion.OperationId == input.OperationId
            && PlayerCreationOperation.TryGetCreatedAt(input.OperationId, out var createdAt)
            && completion.RecoveryExpiresAt == createdAt.Add(PlayerCreationOperation.Lifetime)
            && completion.CompletedAt.Offset == TimeSpan.Zero && completion.RecoveryExpiresAt.Offset == TimeSpan.Zero
            && completion.CompletedAt >= createdAt.AddMinutes(-1) && completion.CompletedAt < completion.RecoveryExpiresAt
            && IsValidPlayer(completion.Player) && completion.Player.ClubId == input.ClubId
            && completion.Player.LifecycleStatus == LifecycleStatus.Active
            && string.Equals(completion.Player.FirstName, input.FirstName, StringComparison.Ordinal)
            && string.Equals(completion.Player.LastName, input.LastName, StringComparison.Ordinal)
            && completion.Player.DateOfBirth == input.DateOfBirth && completion.Player.GraduationYear == input.GraduationYear
            && completion.Player.Gender == input.Gender && completion.Player.JerseyNumber == input.JerseyNumber
            && (completion.Enrollment is null || (completion.Enrollment.CampaignId > 0
                && completion.Enrollment.PlayerCampaignAssignmentId > 0 && !string.IsNullOrWhiteSpace(completion.Enrollment.CampaignName)));

    /// <summary>
    /// Validates the portable invariants of a player success payload.
    /// </summary>
    /// <param name="player">The player to validate.</param>
    /// <param name="expectedPlayerId">The expected player identifier, when known.</param>
    /// <returns><see langword="true"/> when the player is structurally valid.</returns>
    private static bool IsValidPlayer(PlayerDto player, long? expectedPlayerId = null)
        => player is not null
            && player.PlayerId > 0
            && (expectedPlayerId is null || player.PlayerId == expectedPlayerId)
            && player.ClubId > 0
            && !string.IsNullOrWhiteSpace(player.FirstName)
            && !string.IsNullOrWhiteSpace(player.LastName)
            && player.GraduationYear is >= 2000 and <= 2100
            && player.JerseyNumber is null or >= 0 and <= 9999
            && player.Gender is null or Gender.Male or Gender.Female or Gender.Other
            && player.LifecycleStatus is LifecycleStatus.Active or LifecycleStatus.Archived;
}
