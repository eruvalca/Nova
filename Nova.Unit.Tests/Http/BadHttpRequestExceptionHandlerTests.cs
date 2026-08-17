using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Nova.Features.Shared;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Http;

/// <summary>
/// Verifies framework bad-request exceptions are converted to ProblemDetails responses.
/// </summary>
public sealed class BadHttpRequestExceptionHandlerTests
{
    /// <summary>
    /// Identifies the request-payload-too-large status code used to verify status preservation.
    /// </summary>
    private const int PayloadTooLargeStatusCode = 413;

    /// <summary>
    /// Verifies exceptions unrelated to bad requests are left for the next exception handler.
    /// </summary>
    [Fact]
    public async Task BadRequestExceptionHandler_ReturnsFalse_ForNonBadHttpRequestException()
    {
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        var handler = new BadHttpRequestExceptionHandler(problemDetailsService);

        var handled = await handler.TryHandleAsync(
            new DefaultHttpContext(),
            new InvalidOperationException("unexpected"),
            TestContext.Current.CancellationToken);

        handled.ShouldBeFalse();
        await problemDetailsService.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }

    /// <summary>
    /// Verifies a bad request's status and detail are preserved in the ProblemDetails context.
    /// </summary>
    [Fact]
    public async Task BadRequestExceptionHandler_WritesProblemDetails_PreservingStatusCode()
    {
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        ProblemDetailsContext? capturedContext = null;
        problemDetailsService.TryWriteAsync(
                Arg.Do<ProblemDetailsContext>(context => capturedContext = context))
            .Returns(ValueTask.FromResult(true));
        var handler = new BadHttpRequestExceptionHandler(problemDetailsService);
        var httpContext = new DefaultHttpContext();
        var exception = new BadHttpRequestException("payload", StatusCodes.Status400BadRequest);

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            TestContext.Current.CancellationToken);

        handled.ShouldBeTrue();
        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        capturedContext.ShouldNotBeNull();
        capturedContext.ProblemDetails.Status.ShouldBe(StatusCodes.Status400BadRequest);
        capturedContext.ProblemDetails.Detail.ShouldBe("payload");
    }

    /// <summary>
    /// Verifies a non-400 framework status code is preserved rather than hardcoded to bad request.
    /// </summary>
    [Fact]
    public async Task BadRequestExceptionHandler_WritesProblemDetails_PreservingNon400StatusCode()
    {
        var problemDetailsService = Substitute.For<IProblemDetailsService>();
        ProblemDetailsContext? capturedContext = null;
        problemDetailsService.TryWriteAsync(
                Arg.Do<ProblemDetailsContext>(context => capturedContext = context))
            .Returns(ValueTask.FromResult(true));
        var handler = new BadHttpRequestExceptionHandler(problemDetailsService);
        var httpContext = new DefaultHttpContext();
        var exception = new BadHttpRequestException("payload too large", PayloadTooLargeStatusCode);

        var handled = await handler.TryHandleAsync(
            httpContext,
            exception,
            TestContext.Current.CancellationToken);

        handled.ShouldBeTrue();
        httpContext.Response.StatusCode.ShouldBe(PayloadTooLargeStatusCode);
        capturedContext.ShouldNotBeNull();
        capturedContext.ProblemDetails.Status.ShouldBe(PayloadTooLargeStatusCode);
        capturedContext.ProblemDetails.Detail.ShouldBe("payload too large");
    }
}
