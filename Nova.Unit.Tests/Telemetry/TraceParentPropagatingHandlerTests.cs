using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Nova.Client.Telemetry;
using Shouldly;

namespace Nova.Unit.Tests.Telemetry;

public partial class TraceParentPropagatingHandlerTests
{
    [Fact]
    public async Task SendAsync_AddsTraceParentHeader_WhenMissing()
    {
        using var listener = CreateNovaClientListener();
        var capture = new CapturingHandler();
        using var invoker = CreateInvoker(capture);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        var traceParent = GetSingleTraceParentValue(capture.LastRequest);
        MyRegex().IsMatch(traceParent).ShouldBeTrue();
    }

    [Fact]
    public async Task SendAsync_PreservesTraceParentHeader_WhenAlreadyPresent()
    {
        using var listener = CreateNovaClientListener();
        var capture = new CapturingHandler();
        using var invoker = CreateInvoker(capture);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        const string existingTraceParent = "00-11111111111111111111111111111111-2222222222222222-01";
        request.Headers.TryAddWithoutValidation("traceparent", existingTraceParent).ShouldBeTrue();

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        capture.LastRequest.Headers.TryGetValues("traceparent", out var values).ShouldBeTrue();
        values.ShouldHaveSingleItem();
        values.Single().ShouldBe(existingTraceParent);
    }

    [Fact]
    public async Task SendAsync_UsesAmbientParentTraceId_WhenAmbientParentExists()
    {
        using var listener = CreateNovaClientListener();
        var capture = new CapturingHandler();
        using var invoker = CreateInvoker(capture);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        using var ambientParent = new Activity("ambient-parent");
        ambientParent.SetIdFormat(ActivityIdFormat.W3C);
        ambientParent.Start();

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        var traceParent = GetSingleTraceParentValue(capture.LastRequest);
        var propagatedTraceId = traceParent.Split('-')[1];
        propagatedTraceId.ShouldBe(ambientParent.TraceId.ToHexString());
    }

    [Fact]
    public async Task SendAsync_UsesDifferentSpanIds_ForSequentialRequests()
    {
        using var listener = CreateNovaClientListener();
        var capture = new CapturingHandler();
        using var invoker = CreateInvoker(capture);
        using var first = new HttpRequestMessage(HttpMethod.Get, "https://example.test/first");
        using var second = new HttpRequestMessage(HttpMethod.Get, "https://example.test/second");

        using var firstResponse = await invoker.SendAsync(first, TestContext.Current.CancellationToken);
        var firstTraceParent = GetSingleTraceParentValue(capture.LastRequest);

        using var secondResponse = await invoker.SendAsync(second, TestContext.Current.CancellationToken);
        var secondTraceParent = GetSingleTraceParentValue(capture.LastRequest);

        var firstSpanId = firstTraceParent.Split('-')[2];
        var secondSpanId = secondTraceParent.Split('-')[2];
        secondSpanId.ShouldNotBe(firstSpanId);
    }

    private static HttpMessageInvoker CreateInvoker(HttpMessageHandler innerHandler)
    {
        return new HttpMessageInvoker(new TraceParentPropagatingHandler
        {
            InnerHandler = innerHandler
        });
    }

    private static ActivityListener CreateNovaClientListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Nova.Client",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static string GetSingleTraceParentValue(HttpRequestMessage request)
    {
        request.Headers.TryGetValues("traceparent", out var values).ShouldBeTrue();
        values.ShouldHaveSingleItem();
        return values.Single();
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; } = new(HttpMethod.Get, "https://placeholder.test");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [GeneratedRegex("^00-[0-9a-f]{32}-[0-9a-f]{16}-01$")]
    private static partial Regex MyRegex();
}
