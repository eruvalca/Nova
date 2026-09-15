using System.Text.Json;
using Nova.SharedKernel.Results;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Identifies a durable rejection that prevents this exact operation from ever committing.</summary>
public static class PlacementMutationRejection
{
    /// <summary>The expired operation cannot execute again; this does not establish its earlier outcome.</summary>
    public const string ExpiredExtension = "placementExpiredOperationId";

    /// <summary>Recognizes the server's expiry boundary for this exact pending command.</summary>
    public static bool IsExpired(ServiceProblem problem, Guid operationId)
    {
        if (problem.Extensions is null || !problem.Extensions.TryGetValue(ExpiredExtension, out var value)) { return false; }
        return Guid.TryParse(value?.ToString(), out var parsed) && parsed == operationId;
    }
    /// <summary>The ProblemDetails extension containing the rejected operation's identity.</summary>
    public const string OperationIdExtension = "placementNotCommittedOperationId";

    /// <summary>Marks a rejection for storage in the operation's immutable receipt.</summary>
    /// <remarks>Return this proof only after committing the rejection receipt. A status code or absent receipt alone is not proof.</remarks>
    public static ServiceProblem NotCommitted(ServiceProblem problem, Guid operationId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operationId, Guid.Empty);
        var extensions = problem.Extensions is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(problem.Extensions, StringComparer.Ordinal);
        extensions[OperationIdExtension] = operationId.ToString("D");
        return problem with { Extensions = extensions };
    }

    /// <summary>Accepts only a definitive rejection bound to the retained operation, including HTTP-decoded extensions.</summary>
    public static bool IsNotCommitted(ServiceProblem problem, Guid operationId)
    {
        if (operationId == Guid.Empty
            || problem.Kind is not (ServiceProblemKind.Validation or ServiceProblemKind.Forbidden or ServiceProblemKind.NotFound
                or ServiceProblemKind.Conflict or ServiceProblemKind.BadRequest)
            || problem.Extensions is null || !problem.Extensions.TryGetValue(OperationIdExtension, out var value)) { return false; }
        return value switch
        {
            Guid id => id == operationId,
            string text => Guid.TryParseExact(text, "D", out var id) && id == operationId,
            JsonElement { ValueKind: JsonValueKind.String } element => Guid.TryParseExact(element.GetString(), "D", out var id) && id == operationId,
            _ => false
        };
    }
}
