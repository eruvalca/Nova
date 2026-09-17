using System.Text.Json;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Results;

namespace Nova.SharedKernel.Features.Players;

/// <summary>Structured creation conflicts. Only a committed rejection receipt proves that an operation cannot create.</summary>
public static class PlayerCreationProblems
{
    /// <summary>The problem extension identifying the conflict category.</summary>
    public const string ReasonExtension = "playerCreationReason";
    /// <summary>The extension containing an operation whose rejection was durably committed.</summary>
    public const string NotCommittedExtension = "playerCreationNotCommittedOperationId";
    /// <summary>The extension containing a possible duplicate.</summary>
    public const string DuplicateExtension = "playerCreationDuplicate";

    /// <summary>Rejects reuse of an identity belonging to another actor or input without disclosing its result.</summary>
    public static ServiceProblem Mismatch() => ServiceProblem.Conflict(
        "This creation identity belongs to a different request. Retain the original recovery input.",
        new Dictionary<string, object?>(StringComparer.Ordinal) { [ReasonExtension] = "operationMismatch" });

    /// <summary>Expiry prevents further execution but does not establish whether an earlier request committed.</summary>
    public static ServiceProblem Expired() => ServiceProblem.Conflict(
        "This creation operation has expired. Review the Players directory before starting another addition; an earlier attempt may have completed.",
        new Dictionary<string, object?>(StringComparer.Ordinal) { [ReasonExtension] = "expired" });

    /// <summary>Builds a duplicate rejection; return its definitive marker only after committing the rejection receipt.</summary>
    public static ServiceProblem Duplicate(Guid operationId, long playerId, LifecycleStatus status) => ServiceProblem.Conflict(
        "A player with this name and date of birth already exists. Open that record or correct the player details.",
        new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ReasonExtension] = "possibleDuplicate",
            [NotCommittedExtension] = operationId.ToString("D"),
            [DuplicateExtension] = new PlayerCreationDuplicate { PlayerId = playerId, LifecycleStatus = status }
        });

    /// <summary>Recognizes definitive rejection only for the retained operation, including HTTP-decoded extensions.</summary>
    public static bool IsNotCommitted(ServiceProblem problem, Guid operationId)
        => operationId != Guid.Empty && problem.Kind == ServiceProblemKind.Conflict
            && problem.Extensions is not null
            && problem.Extensions.TryGetValue(ReasonExtension, out var reason)
            && string.Equals(ReadString(reason), "possibleDuplicate", StringComparison.Ordinal)
            && TryGetDuplicate(problem, out _)
            && problem.Extensions.TryGetValue(NotCommittedExtension, out var value)
            && ReadString(value) is { } text && Guid.TryParseExact(text, "D", out var id) && id == operationId;

    /// <summary>Rejects missing or contradictory conflict evidence before a consumer releases pending work.</summary>
    public static bool IsValidConflict(ServiceProblem problem, Guid operationId)
    {
        if (problem.Kind != ServiceProblemKind.Conflict || problem.Extensions is null
            || !problem.Extensions.TryGetValue(ReasonExtension, out var reason)) { return false; }
        return ReadString(reason) switch
        {
            "possibleDuplicate" => IsNotCommitted(problem, operationId),
            "expired" or "operationMismatch" => !problem.Extensions.ContainsKey(NotCommittedExtension)
                && !problem.Extensions.ContainsKey(DuplicateExtension),
            _ => false
        };
    }

    /// <summary>Recognizes valid expiry evidence without asserting whether an earlier attempt committed.</summary>
    public static bool IsExpired(ServiceProblem problem)
        => problem.Extensions is not null
            && problem.Extensions.TryGetValue(ReasonExtension, out var reason)
            && string.Equals(ReadString(reason), "expired", StringComparison.Ordinal)
            && IsValidConflict(problem, Guid.Empty);

    /// <summary>Reads a bounded duplicate destination from either direct or HTTP-decoded problems.</summary>
    public static bool TryGetDuplicate(ServiceProblem problem, out PlayerCreationDuplicate? duplicate)
    {
        duplicate = null;
        if (problem.Kind != ServiceProblemKind.Conflict || problem.Extensions is null
            || !problem.Extensions.TryGetValue(DuplicateExtension, out var value)) { return false; }
        try
        {
            var candidate = value switch
            {
                PlayerCreationDuplicate typed => typed,
                JsonElement json => json.Deserialize<PlayerCreationDuplicate>(JsonSerializerOptions.Web),
                _ => null
            };
            if (candidate is null || candidate.PlayerId <= 0
                || candidate.LifecycleStatus is not (LifecycleStatus.Active or LifecycleStatus.Archived)) { return false; }
            duplicate = candidate;
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static string? ReadString(object? value) => value switch
    {
        string text => text,
        JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
        _ => null
    };
}

/// <summary>A tenant-visible record staff can inspect to resolve a possible duplicate.</summary>
public sealed record PlayerCreationDuplicate
{
    /// <summary>The existing player's identity.</summary>
    public required long PlayerId { get; init; }
    /// <summary>The lifecycle status observed at rejection.</summary>
    public required LifecycleStatus LifecycleStatus { get; init; }
}
