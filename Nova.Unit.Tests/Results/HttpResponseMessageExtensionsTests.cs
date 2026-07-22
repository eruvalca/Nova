using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Results;

/// <summary>
/// Tests for <see cref="HttpResponseMessageExtensions"/>: status-code to problem-kind mapping,
/// single-read body parsing of ProblemDetails/ValidationProblemDetails, and tolerance of
/// empty or non-JSON bodies.
/// </summary>
public class HttpResponseMessageExtensionsTests
{
    [Fact]
    public async Task ToServiceProblemAsync_ReturnsValidation_For400WithErrorsBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                detail = "Please correct the validation errors.",
                errors = new Dictionary<string, string[]>
                {
                    ["file"] = ["The photo is too large.", "The photo must be an image."]
                }
            })
        };

        var problem = await response.ToServiceProblemAsync(TestContext.Current.CancellationToken);

        problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        problem.Detail.ShouldBe("Please correct the validation errors.");
        problem.Errors.ShouldNotBeNull();
        problem.Errors["file"].ShouldBe(["The photo is too large.", "The photo must be an image."]);
    }

    [Fact]
    public async Task ToServiceProblemAsync_ReturnsBadRequest_For400WithPlainProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { detail = "The image could not be processed." })
        };

        var problem = await response.ToServiceProblemAsync(TestContext.Current.CancellationToken);

        problem.Kind.ShouldBe(ServiceProblemKind.BadRequest);
        problem.Detail.ShouldBe("The image could not be processed.");
        problem.Errors.ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ServiceProblemKind.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, ServiceProblemKind.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, ServiceProblemKind.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError, ServiceProblemKind.ServerError)]
    [InlineData(HttpStatusCode.BadGateway, ServiceProblemKind.ServerError)]
    public async Task ToServiceProblemAsync_MapsStatusCode_ToProblemKind(
        HttpStatusCode statusCode,
        ServiceProblemKind expectedKind)
    {
        using var response = new HttpResponseMessage(statusCode)
        {
            Content = JsonContent.Create(new { detail = "Something went wrong." })
        };

        var problem = await response.ToServiceProblemAsync(TestContext.Current.CancellationToken);

        problem.Kind.ShouldBe(expectedKind);
        problem.Detail.ShouldBe("Something went wrong.");
    }

    [Fact]
    public async Task ToServiceProblemAsync_ReturnsValidation_For422WithErrorsBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = JsonContent.Create(new
            {
                detail = "Please correct the validation errors.",
                errors = new Dictionary<string, string[]>
                {
                    ["firstName"] = ["First name is required."]
                }
            })
        };

        var problem = await response.ToServiceProblemAsync(TestContext.Current.CancellationToken);

        problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        problem.Detail.ShouldBe("Please correct the validation errors.");
        problem.Errors.ShouldNotBeNull();
        problem.Errors["firstName"].ShouldBe(["First name is required."]);
    }

    [Fact]
    public async Task ToServiceProblemAsync_ReturnsValidation_For422WithoutErrors()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = JsonContent.Create(new { detail = "Unprocessable." })
        };

        var problem = await response.ToServiceProblemAsync(TestContext.Current.CancellationToken);

        problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        problem.Errors.ShouldNotBeNull();
        problem.Errors.ShouldBeEmpty();
    }

    [Fact]
    public async Task ToServiceProblemAsync_ReturnsNullDetail_ForEmptyBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound);

        var problem = await response.ToServiceProblemAsync(TestContext.Current.CancellationToken);

        problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        problem.Detail.ShouldBeNull();
    }

    [Fact]
    public async Task ToServiceProblemAsync_DoesNotThrow_ForNonJsonBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html><body>Server Error</body></html>", Encoding.UTF8, "text/html")
        };

        var problem = await response.ToServiceProblemAsync(TestContext.Current.CancellationToken);

        problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        problem.Detail.ShouldBeNull();
    }

    [Fact]
    public async Task ToServiceProblemAsync_ReadsBodyOnce_FromRealStringContent()
    {
        // Regression: the previous implementation read the body twice, which threw
        // ObjectDisposedException on the second read for real response content. A single
        // conversion must surface both detail and errors from one body read.
        const string body = """
            {
              "title": "One or more validation errors occurred.",
              "status": 400,
              "detail": "Validation failed.",
              "errors": { "email": ["Email is required."] },
              "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
            }
            """;
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json")
        };

        var problem = await response.ToServiceProblemAsync(TestContext.Current.CancellationToken);

        problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        problem.Detail.ShouldBe("Validation failed.");
        problem.Errors.ShouldNotBeNull();
        problem.Errors["email"].ShouldBe(["Email is required."]);
    }
}
