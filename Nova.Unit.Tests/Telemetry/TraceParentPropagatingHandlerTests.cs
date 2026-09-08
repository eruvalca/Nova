using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Nova.Client.Telemetry;
using Shouldly;

namespace Nova.Unit.Tests.Telemetry;

public partial class TraceParentPropagatingHandlerTests
{
    [Fact]
    public async Task SendAsyncAddsTraceParentHeaderWhenMissingAsync()
    {
        using var listener = CreateNovaClientListener();
        using var capture = new CapturingHandler();
        using var invoker = CreateInvoker(capture);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        var traceParent = GetSingleTraceParentValue(capture.LastRequest);
        MyRegex().IsMatch(traceParent).ShouldBeTrue();
    }

    [Fact]
    public async Task SendAsyncPreservesTraceParentHeaderWhenAlreadyPresentAsync()
    {
        using var listener = CreateNovaClientListener();
        using var capture = new CapturingHandler();
        using var invoker = CreateInvoker(capture);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        const string ExistingTraceParent = "00-11111111111111111111111111111111-2222222222222222-01";
        request.Headers.TryAddWithoutValidation("traceparent", ExistingTraceParent).ShouldBeTrue();

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        capture.LastRequest.Headers.TryGetValues("traceparent", out var values).ShouldBeTrue();
        values.ShouldHaveSingleItem();
        values.Single().ShouldBe(ExistingTraceParent);
    }

    [Fact]
    public async Task SendAsyncUsesAmbientParentTraceIdWhenAmbientParentExistsAsync()
    {
        using var listener = CreateNovaClientListener();
        using var capture = new CapturingHandler();
        using var invoker = CreateInvoker(capture);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        using var ambientParent = new System.Diagnostics.Activity("ambient-parent");
        ambientParent.SetIdFormat(ActivityIdFormat.W3C);
        ambientParent.Start();

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        var traceParent = GetSingleTraceParentValue(capture.LastRequest);
        var propagatedTraceId = traceParent.Split('-')[1];
        propagatedTraceId.ShouldBe(ambientParent.TraceId.ToHexString());
    }

    [Fact]
    public async Task SendAsyncUsesDifferentSpanIdsForSequentialRequestsAsync()
    {
        using var listener = CreateNovaClientListener();
        using var capture = new CapturingHandler();
        using var invoker = CreateInvoker(capture);
        using var first = new HttpRequestMessage(HttpMethod.Get, "https://example.test/first");
        using var second = new HttpRequestMessage(HttpMethod.Get, "https://example.test/second");

        using var firstResponse = await invoker.SendAsync(first, TestContext.Current.CancellationToken);
        var firstTraceParent = GetSingleTraceParentValue(capture.LastRequest);

        using var secondResponse = await invoker.SendAsync(second, TestContext.Current.CancellationToken);
        var secondTraceParent = GetSingleTraceParentValue(capture.LastRequest);

        var firstSpanId = firstTraceParent.Split('-')[2];
        var secondSpanId = secondTraceParent.Split('-')[2];
        secondSpanId.ShouldNotBe(firstSpanId, StringComparer.Ordinal);
    }

    private static HttpMessageInvoker CreateInvoker(HttpMessageHandler innerHandler)
    {
#pragma warning disable CA2000 // The returned HttpMessageInvoker owns and disposes its handler chain.
        return new HttpMessageInvoker(new TraceParentPropagatingHandler
#pragma warning restore CA2000
        {
            InnerHandler = innerHandler
        });
    }

    private static ActivityListener CreateNovaClientListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Nova.Client", StringComparison.Ordinal),
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

    [GeneratedRegex("^00-[0-9a-f]{32}-[0-9a-f]{16}-01$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MyRegex();
}
