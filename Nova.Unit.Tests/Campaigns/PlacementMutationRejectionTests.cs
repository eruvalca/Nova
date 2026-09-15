using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class PlacementMutationRejectionTests
{
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
    public void FutureClockGuidanceIsBoundToTheOperationAndNeverProvesSettlement(string representation, bool expected)
    {
        var operation = Guid.CreateVersion7();
        var problem = ServiceProblem.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal)) with
        {
            Extensions = new Dictionary<string, object?>(StringComparer.Ordinal)
            { [PlacementMutationRejection.FutureOperationIdExtension] = MarkerValue(representation, operation) }
        };

        PlacementMutationRejection.IsFutureDated(problem, operation).ShouldBe(expected);
        PlacementMutationRejection.IsFutureDated(problem, Guid.Empty).ShouldBeFalse();
        PlacementMutationRejection.IsFutureDated(problem with { Kind = ServiceProblemKind.Conflict }, operation).ShouldBeFalse();
        PlacementMutationRejection.IsNotCommitted(problem, operation).ShouldBeFalse();
        PlacementMutationRejection.IsExpired(problem, operation).ShouldBeFalse();
    }

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
        var marked = PlacementMutationRejection.NotCommitted(original, operation);

        PlacementMutationRejection.IsNotCommitted(marked, operation).ShouldBeTrue();
        PlacementMutationRejection.IsNotCommitted(marked, Guid.CreateVersion7()).ShouldBeFalse();
        PlacementMutationRejection.IsNotCommitted(marked, Guid.Empty).ShouldBeFalse();
        PlacementMutationRejection.IsNotCommitted(original, operation).ShouldBeFalse();
        marked.Kind.ShouldBe(kind);
        marked.Detail.ShouldBe(original.Detail);
        marked.Errors.ShouldBe(errors);
        marked.Extensions!["traceId"].ShouldBe("original-trace");
        original.Extensions!.ContainsKey(PlacementMutationRejection.OperationIdExtension).ShouldBeFalse();
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
        var problem = ServiceProblem.Conflict(extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { [PlacementMutationRejection.OperationIdExtension] = value });

        PlacementMutationRejection.IsNotCommitted(problem, operation).ShouldBe(expected);
        PlacementMutationRejection.IsNotCommitted(problem with { Kind = ServiceProblemKind.ServerError }, operation).ShouldBeFalse();
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
                [PlacementMutationRejection.OperationIdExtension] = MarkerValue(representation, operation)
            })
        };
        var problem = await response.ToServiceProblemAsync(TestContext.Current.CancellationToken);

        problem.Detail.ShouldBe("Original problem");
        PlacementMutationRejection.IsNotCommitted(problem, operation).ShouldBe(expected);
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



