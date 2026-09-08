using System.Text.Json;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Verifies player lifecycle ProblemDetails extensions are parsed through a non-throwing try API.
/// </summary>
public sealed class PlayerLifecycleProblemExtensionsTests
{
    /// <summary>
    /// Verifies malformed blocker array elements return false instead of propagating a JSON parse exception.
    /// </summary>
    [Fact]
    public void TryGetArchiveBlockersReturnsFalseForMalformedArrayElements()
    {
        var malformed = JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                campaignId = "not-a-number",
                campaignName = "Malformed",
#pragma warning disable CA1861 // Each test owns its expected data and fixture arrays; these are not repeated production allocations.
                participationIds = new[] { 1L }
#pragma warning restore CA1861
            }
        });
        var problem = CreateProblem(malformed);

        var parsed = problem.TryGetArchiveBlockers(out var blockers);

        parsed.ShouldBeFalse();
        blockers.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies a non-array extension value returns false without attempting deserialization.
    /// </summary>
    [Fact]
    public void TryGetArchiveBlockersReturnsFalseForNonArrayValue()
    {
        var wrongShape = JsonSerializer.SerializeToElement(new { campaignId = 1L });
        var problem = CreateProblem(wrongShape);

        var parsed = problem.TryGetArchiveBlockers(out var blockers);

        parsed.ShouldBeFalse();
        blockers.ShouldBeEmpty();
    }

    /// <summary>
    /// Creates a conflict problem carrying the supplied archive-blocker extension value.
    /// </summary>
    /// <param name="extensionValue">The raw extension value under test.</param>
    /// <returns>A conflict service problem containing the extension.</returns>
    private static ServiceProblem CreateProblem(object extensionValue)
        => ServiceProblem.Conflict(
            "Archive blockers were returned.",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [PlayerLifecycleProblemExtensions.ArchiveBlockersExtensionName] = extensionValue
            });
}
