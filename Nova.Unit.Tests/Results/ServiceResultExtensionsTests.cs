using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nova.Features.Shared;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Results;

/// <summary>
/// Verifies server-side service problems execute as correlated RFC 7807 responses.
/// </summary>
public sealed class ServiceResultExtensionsTests
{
    /// <summary>
    /// Verifies a forbidden service problem executes as HTTP 403 and survives the client round trip.
    /// </summary>
    [Fact]
    public async Task ToHttpResult_ExecutesForbiddenProblemDetails_WithTraceId()
    {
        using var activity = new Activity(nameof(ToHttpResult_ExecutesForbiddenProblemDetails_WithTraceId)).Start();
        using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await using var responseBodyStream = new MemoryStream();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            Response = { Body = responseBodyStream }
        };

        var result = ServiceProblem.Forbidden("Roster access is forbidden.").ToHttpResult();

        await result.ExecuteAsync(httpContext);

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        httpContext.Response.ContentType.ShouldStartWith("application/problem+json");

        responseBodyStream.Position = 0;
        using var reader = new StreamReader(responseBodyStream, Encoding.UTF8, leaveOpen: true);
        var responseBody = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(responseBody);
        document.RootElement.GetProperty("detail").GetString().ShouldBe("Roster access is forbidden.");
        document.RootElement.GetProperty("status").GetInt32().ShouldBe(StatusCodes.Status403Forbidden);
        document.RootElement.GetProperty("traceId").GetString().ShouldBe(activity.TraceId.ToString());

        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/problem+json")
        };
        var roundTripProblem = await response.ToServiceProblemAsync(TestContext.Current.CancellationToken);
        roundTripProblem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }
}
