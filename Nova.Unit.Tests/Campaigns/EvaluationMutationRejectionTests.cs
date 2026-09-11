using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class EvaluationMutationRejectionTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(ServiceProblemKind.Validation)]
    [InlineData(ServiceProblemKind.Forbidden)]
    [InlineData(ServiceProblemKind.NotFound)]
    [InlineData(ServiceProblemKind.Conflict)]
    [InlineData(ServiceProblemKind.BadRequest)]
    public void ConfirmedNoncommitIsBoundToOperationAndPreservesProblem(ServiceProblemKind kind)
    {
        var operation = Guid.CreateVersion7();
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["Content"] = ["Correct this content"] };
        var original = new ServiceProblem(kind, "Known rejection", errors, new Dictionary<string, object?>(StringComparer.Ordinal) { ["traceId"] = "original-trace" });
        var marked = EvaluationMutationRejection.NotCommitted(original, operation);

        EvaluationMutationRejection.IsNotCommitted(marked, operation).ShouldBeTrue();
        EvaluationMutationRejection.IsNotCommitted(marked, Guid.CreateVersion7()).ShouldBeFalse();
        EvaluationMutationRejection.IsNotCommitted(marked, Guid.Empty).ShouldBeFalse();
        EvaluationMutationRejection.IsNotCommitted(original, operation).ShouldBeFalse();
        marked.Kind.ShouldBe(kind);
        marked.Detail.ShouldBe(original.Detail);
        marked.Errors.ShouldBe(errors);
        marked.Extensions!["traceId"].ShouldBe("original-trace");
        original.Extensions!.ContainsKey(RecoveryRejectionCases.Marker).ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("guid", true)]
    [InlineData("string", true)]
    [InlineData("json-string", true)]
    [InlineData("null", false)]
    [InlineData("empty", false)]
    [InlineData("wrong", false)]
    [InlineData("boolean", false)]
    [InlineData("number", false)]
    [InlineData("object", false)]
    [InlineData("array", false)]
    [InlineData("undefined", false)]
    public void OnlyMatchingNonemptyGuidValuesProveNoncommit(string representation, bool expected)
    {
        var operation = Guid.CreateVersion7();
        var value = MarkerValue(representation, operation);
        var problem = ServiceProblem.Conflict(extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { [RecoveryRejectionCases.Marker] = value });

        EvaluationMutationRejection.IsNotCommitted(problem, operation).ShouldBe(expected);
        EvaluationMutationRejection.IsNotCommitted(problem with { Kind = ServiceProblemKind.ServerError }, operation).ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("string", true)]
    [InlineData("null", false)]
    [InlineData("wrong", false)]
    [InlineData("object", false)]
    [InlineData("array", false)]
    [InlineData("number", false)]
    public async Task HttpProblemParsingPreservesStrictOperationProofAsync(string representation, bool expected)
    {
        var operation = Guid.CreateVersion7();
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["detail"] = "Original problem",
                ["traceId"] = "wire-trace",
                [RecoveryRejectionCases.Marker] = MarkerValue(representation, operation)
            })
        };
        var problem = await response.ToServiceProblemAsync(TestContext.Current.CancellationToken);

        problem.Detail.ShouldBe("Original problem");
        EvaluationMutationRejection.IsNotCommitted(problem, operation).ShouldBe(expected);
        ((JsonElement)problem.Extensions!["traceId"]!).GetString().ShouldBe("wire-trace");
    }

    private static object? MarkerValue(string representation, Guid operation) => representation switch
    {
        "guid" => operation,
        "string" => operation.ToString("D"),
        "json-string" => JsonSerializer.SerializeToElement(operation),
        "null" => null,
        "empty" => Guid.Empty,
        "wrong" => Guid.CreateVersion7().ToString("D"),
        "boolean" => true,
        "number" => 1,
        "object" => JsonSerializer.SerializeToElement(new { OperationId = operation }),
        "array" => JsonSerializer.SerializeToElement(new[] { operation }),
        "undefined" => default(JsonElement),
        _ => throw new ArgumentOutOfRangeException(nameof(representation))
    };
}

internal static class RecoveryRejectionCases
{
    internal const string Marker = "evaluationNotCommittedOperationId";
    internal static string Detail(string rejection) => string.Equals(rejection, "expired", StringComparison.Ordinal)
        ? "The 24-hour recovery window has expired." : "Recovery could not establish this operation's outcome.";

    internal static ServiceProblem Problem(string rejection, Guid operation) => rejection switch
    {
        "forbidden" => ServiceProblem.Forbidden(Detail(rejection)),
        "conflict" or "expired" => ServiceProblem.Conflict(Detail(rejection)),
        "wrong-marker" => EvaluationMutationRejection.NotCommitted(ServiceProblem.Conflict(Detail(rejection)), Guid.CreateVersion7()),
        "malformed-marker" => ServiceProblem.Conflict(Detail(rejection), extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { [Marker] = new { OperationId = operation } }),
        _ => throw new ArgumentOutOfRangeException(nameof(rejection))
    };
}
