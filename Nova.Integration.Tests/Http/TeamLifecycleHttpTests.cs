using System.Net;
using System.Net.Http.Json;
using Nova.Integration.Tests.Data;
using Nova.Shared.Teams;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Covers the authentication boundary for team lifecycle and graduation-cutoff endpoints.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TeamLifecycleHttpTests(NovaAppHostFixture fixture)
{
    [Theory]
    [InlineData("archive")]
    [InlineData("restore")]
    [InlineData("graduation-year")]
    public async Task TeamLifecycleEndpoints_ReturnUnauthorized_ForAnonymous(string operation)
    {
        using var client = fixture.CreateNovaHttpClient();
        var cancellationToken = TestContext.Current.CancellationToken;
        var teamId = 999_999L;

        using var response = operation == "graduation-year"
            ? await client.PutAsJsonAsync(
                TeamEndpoints.UpdateGraduationYearUrl(teamId),
                new UpdateTeamGraduationYearInput { TeamId = teamId, GraduationYear = 2030 },
                cancellationToken)
            : await client.PostAsync(
                operation == "archive"
                    ? TeamEndpoints.ArchiveUrl(teamId)
                    : TeamEndpoints.RestoreUrl(teamId),
                content: null,
                cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
