using System.Net;
using System.Net.Http.Json;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies the WebAssembly campaign lifecycle client route and response contract.
/// </summary>
public sealed class HttpCampaignLifecycleServiceTests
{
    private const long CampaignId = 42;

    /// <summary>Verifies open posts the operation and accepts a consistent immutable receipt.</summary>
    [Fact]
    public async Task OpenAsync_PostsToSharedUrl_AndReturnsValidatedReceipt()
    {
        var operationId = Guid.NewGuid();
        var receipt = new OpenCampaignResult(
            operationId,
            CampaignId,
            DateTimeOffset.UtcNow,
            7,
            3,
            0,
            [CampaignOpeningWarning.NoActiveTeams]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(receipt)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignLifecycleService(http).OpenAsync(
            CampaignId,
            new OpenCampaignInput { OperationId = operationId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OperationId.ShouldBe(receipt.OperationId);
        result.Value.CampaignId.ShouldBe(receipt.CampaignId);
        result.Value.OpenedAt.ShouldBe(receipt.OpenedAt);
        result.Value.OpenedByUserId.ShouldBe(receipt.OpenedByUserId);
        result.Value.EnrolledPlayerCount.ShouldBe(receipt.EnrolledPlayerCount);
        result.Value.ActiveTeamCount.ShouldBe(receipt.ActiveTeamCount);
        result.Value.Warnings.ShouldBe(receipt.Warnings);
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.OpenUrl(CampaignId));
    }

    /// <summary>Verifies an inconsistent success payload is rejected as an internal problem.</summary>
    [Fact]
    public async Task OpenAsync_RejectsInconsistentSuccessPayload()
    {
        var operationId = Guid.NewGuid();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new OpenCampaignResult(
                operationId,
                CampaignId,
                DateTimeOffset.UtcNow,
                7,
                3,
                1,
                [CampaignOpeningWarning.NoActiveTeams]))
        };
        using var http = new HttpClient(new FakeHttpMessageHandler(response))
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignLifecycleService(http).OpenAsync(
            CampaignId,
            new OpenCampaignInput { OperationId = operationId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies Draft deletion uses DELETE and accepts the idempotent no-content response.</summary>
    [Fact]
    public async Task DeleteDraftAsync_DeletesSharedUrl_AndReturnsSuccess_OnNoContent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignLifecycleService(http).DeleteDraftAsync(
            CampaignId,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.DeleteDraftUrl(CampaignId));
    }

    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        /// <summary>Gets the last request sent by the client.</summary>
        public HttpRequestMessage? LastRequest { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Verifies a successful close POSTs to the shared close URL and returns success on 204.
    /// </summary>
    [Fact]
    public async Task CloseAsync_PostsToSharedCloseUrl_AndReturnsSuccess_OnNoContent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignLifecycleService(http).CloseAsync(
            CampaignId,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.CloseUrl(CampaignId));
    }

    /// <summary>
    /// Verifies a successful reopen POSTs to the shared reopen URL and returns success on 204.
    /// </summary>
    [Fact]
    public async Task ReopenAsync_PostsToSharedReopenUrl_AndReturnsSuccess_OnNoContent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignLifecycleService(http).ReopenAsync(
            CampaignId,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.ReopenUrl(CampaignId));
    }

    /// <summary>
    /// Verifies a forbidden ProblemDetails response is propagated correctly.
    /// </summary>
    [Fact]
    public async Task CloseAsync_ReturnsForbidden_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new
            {
                title = "Forbidden",
                status = 403,
                detail = "You must be a club administrator to close a campaign."
            })
        };
        using var http = new HttpClient(new FakeHttpMessageHandler(response))
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignLifecycleService(http).CloseAsync(
            CampaignId,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies a non-disclosing not-found ProblemDetails response is propagated correctly.
    /// </summary>
    [Fact]
    public async Task CloseAsync_ReturnsNotFound_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { title = "Not Found", status = 404 })
        };
        using var http = new HttpClient(new FakeHttpMessageHandler(response))
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignLifecycleService(http).CloseAsync(
            CampaignId,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies a conflict ProblemDetails response carrying condition-keyed blockers preserves the
    /// structured error groups.
    /// </summary>
    [Fact]
    public async Task CloseAsync_ReturnsConflict_WithStructuredErrors_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Conflict",
                status = 409,
                detail = "Resolve all campaign close blockers before closing this campaign.",
                errors = new Dictionary<string, string[]>
                {
                    ["outcomes"] = ["Every participant must have a final outcome before closing. Found 1 undecided participation record(s)."],
                    ["eligibility"] = ["Every assigned participant must remain eligible for their team. Ineligible assignment ids: 903."],
                    ["archivedTeams"] = ["Assigned participants cannot reference archived teams. Blocked assignment ids: 904."]
                }
            })
        };
        using var http = new HttpClient(new FakeHttpMessageHandler(response))
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignLifecycleService(http).CloseAsync(
            CampaignId,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors!.ShouldContainKey("outcomes");
        result.Problem.Errors.ShouldContainKey("eligibility");
        result.Problem.Errors.ShouldContainKey("archivedTeams");
    }

    /// <summary>
    /// Verifies a conflict ProblemDetails response without structured errors is propagated correctly.
    /// </summary>
    [Fact]
    public async Task ReopenAsync_ReturnsConflict_WithoutErrors_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Conflict",
                status = 409,
                detail = "The campaign is already active."
            })
        };
        using var http = new HttpClient(new FakeHttpMessageHandler(response))
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignLifecycleService(http).ReopenAsync(
            CampaignId,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Errors.ShouldBeNull();
    }
}
