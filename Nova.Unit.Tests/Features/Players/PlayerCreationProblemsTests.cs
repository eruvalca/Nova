using System.Text.Json;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

public sealed class PlayerCreationProblemsTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("possibleDuplicate", false)]
    [InlineData("possibleDuplicate", true)]
    [InlineData("expired", false)]
    [InlineData("expired", true)]
    [InlineData("operationMismatch", false)]
    [InlineData("operationMismatch", true)]
    public void StructuredErrorsInvalidateCreationConflictEvidence(string reason, bool decoded)
    {
        var operationId = Guid.CreateVersion7();
        var problem = reason switch
        {
            "possibleDuplicate" => PlayerCreationProblems.Duplicate(operationId, 7, LifecycleStatus.Active),
            "expired" => PlayerCreationProblems.Expired(),
            _ => PlayerCreationProblems.Mismatch()
        };
        problem = Decode(problem with
        {
            Errors = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["FirstName"] = ["Invalid value"] }
        }, decoded);

        PlayerCreationProblems.IsValidConflict(problem, operationId).ShouldBeFalse();
        PlayerCreationProblems.IsNotCommitted(problem, operationId).ShouldBeFalse();
        PlayerCreationProblems.IsExpired(problem).ShouldBeFalse();
        PlayerCreationProblems.TryGetDuplicate(problem, out var duplicate).ShouldBeFalse();
        duplicate.ShouldBeNull();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DuplicateWithoutStructuredErrorsRemainsDefinitive(bool emptyErrors, bool decoded)
    {
        var operationId = Guid.CreateVersion7();
        var problem = Decode(PlayerCreationProblems.Duplicate(operationId, 7, LifecycleStatus.Archived) with
        {
            Errors = emptyErrors ? new Dictionary<string, string[]>(StringComparer.Ordinal) : null
        }, decoded);

        PlayerCreationProblems.IsValidConflict(problem, operationId).ShouldBeTrue();
        PlayerCreationProblems.IsNotCommitted(problem, operationId).ShouldBeTrue();
        PlayerCreationProblems.IsExpired(problem).ShouldBeFalse();
        PlayerCreationProblems.TryGetDuplicate(problem, out var duplicate).ShouldBeTrue();
        duplicate.ShouldBe(new PlayerCreationDuplicate { PlayerId = 7, LifecycleStatus = LifecycleStatus.Archived });
    }

    private static ServiceProblem Decode(ServiceProblem problem, bool decoded)
        => decoded ? JsonSerializer.Deserialize<ServiceProblem>(JsonSerializer.Serialize(problem, JsonSerializerOptions.Web), JsonSerializerOptions.Web) : problem;
}
