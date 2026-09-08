using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services.Seasons;
using Nova.SharedKernel.Features.Seasons;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Seasons;

/// <summary>Verifies season query URLs and strict success-body validation.</summary>
public sealed class HttpSeasonQueryServiceTests
{
    /// <summary>Verifies list paging is encoded on the first-class season collection route.</summary>
    [Fact]
    public async Task ListAsyncGetsBoundedPagingRouteAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonPageResult
            {
                Items = [],
                Page = 2,
                PageSize = 50,
                TotalCount = 0
            })
        };
        using var handler = new RecordingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpSeasonQueryService(http).ListAsync(
            new GetSeasonListInput { Page = 2, PageSize = 50 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/seasons?page=2&pageSize=50");
    }

    /// <summary>Verifies invalid list paging is rejected before transport.</summary>
    [Fact]
    public async Task ListAsyncReturnsValidationProblemForInvalidInputAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var handler = new RecordingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpSeasonQueryService(http).ListAsync(
            new GetSeasonListInput { Page = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.LastRequest.ShouldBeNull();
    }

    /// <summary>Verifies list responses must identify the exact effective requested paging values.</summary>
    /// <param name="responsePage">The page returned by the malformed response.</param>
    /// <param name="responsePageSize">The page size returned by the malformed response.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1, 50)]
    [InlineData(2, 20)]
    public async Task ListAsyncReturnsServerErrorWhenResponsePagingDoesNotMatchRequestAsync(
        int responsePage,
        int responsePageSize)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonPageResult
            {
                Items = [],
                Page = responsePage,
                PageSize = responsePageSize,
                TotalCount = 0
            })
        };
        using var httpHandler = new RecordingHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonQueryService(http).ListAsync(
            new GetSeasonListInput { Page = 2, PageSize = 50 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies malformed successful list payloads become protocol failures.</summary>
    [Fact]
    public async Task ListAsyncReturnsServerErrorForInvalidSuccessBodyAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonPageResult
            {
                Items =
                [
                    new SeasonSummary
                    {
                        SeasonId = 7,
                        Name = "Season",
                        StartDate = new DateOnly(2026, 1, 1),
                        IsCurrent = true,
                        ConcurrencyToken = Guid.Empty
                    }
                ],
                Page = 1,
                PageSize = 20,
                TotalCount = 1
            })
        };
        using var httpHandler = new RecordingHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonQueryService(http).ListAsync(
            new GetSeasonListInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies an eventually consistent total may trail the already-read page.</summary>
    [Fact]
    public async Task ListAsyncAcceptsPageWhenEventuallyConsistentTotalIsSmallerAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonPageResult
            {
                Items =
                [
                    new SeasonSummary
                    {
                        SeasonId = 7,
                        Name = "Season",
                        StartDate = new DateOnly(2026, 1, 1),
                        IsCurrent = true,
                        ConcurrencyToken = Guid.NewGuid()
                    }
                ],
                Page = 1,
                PageSize = 20,
                TotalCount = 0
            })
        };
        using var httpHandler = new RecordingHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonQueryService(http).ListAsync(
            new GetSeasonListInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(1);
        result.Value.TotalCount.ShouldBe(0);
    }

    /// <summary>Verifies an eventually consistent total must still be nonnegative.</summary>
    [Fact]
    public async Task ListAsyncReturnsServerErrorWhenTotalIsNegativeAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonPageResult
            {
                Items = [],
                Page = 1,
                PageSize = 20,
                TotalCount = -1
            })
        };
        using var httpHandler = new RecordingHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonQueryService(http).ListAsync(
            new GetSeasonListInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies a current season cannot follow a historical row.</summary>
    [Fact]
    public async Task ListAsyncReturnsServerErrorWhenCurrentSeasonIsBehindHistoryAsync()
    {
        await AssertInvalidSeasonOrderAsync(
        [
            NewSeasonSummary(8, new DateOnly(2027, 1, 1), isCurrent: false),
            NewSeasonSummary(7, new DateOnly(2026, 1, 1), isCurrent: true)
        ]);
    }

    /// <summary>Verifies historical rows remain newest-first by start date.</summary>
    [Fact]
    public async Task ListAsyncReturnsServerErrorWhenHistoricalStartDatesAreAscendingAsync()
    {
        await AssertInvalidSeasonOrderAsync(
        [
            NewSeasonSummary(7, new DateOnly(2026, 1, 1), isCurrent: false),
            NewSeasonSummary(8, new DateOnly(2027, 1, 1), isCurrent: false)
        ]);
    }

    /// <summary>Verifies equal-date historical rows remain identifier-descending.</summary>
    [Fact]
    public async Task ListAsyncReturnsServerErrorWhenHistoricalIdentifiersAreAscendingAsync()
    {
        var startDate = new DateOnly(2027, 1, 1);
        await AssertInvalidSeasonOrderAsync(
        [
            NewSeasonSummary(7, startDate, isCurrent: false),
            NewSeasonSummary(8, startDate, isCurrent: false)
        ]);
    }

    /// <summary>Verifies the current row cannot appear after the first page.</summary>
    [Fact]
    public async Task ListAsyncReturnsServerErrorWhenCurrentSeasonAppearsAfterFirstPageAsync()
    {
        await AssertInvalidSeasonOrderAsync(
            [NewSeasonSummary(7, new DateOnly(2027, 1, 1), isCurrent: true)],
            page: 2);
    }

    /// <summary>Verifies detail paging uses the season identifier and campaign paging names.</summary>
    [Fact]
    public async Task GetAsyncGetsSeasonDetailPagingRouteAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonDetailResult
            {
                Season = new SeasonSummary
                {
                    SeasonId = 7,
                    Name = "Season",
                    StartDate = new DateOnly(2026, 1, 1),
                    IsCurrent = false,
                    ConcurrencyToken = Guid.NewGuid()
                },
                Campaigns = [],
                CampaignPage = 3,
                CampaignPageSize = 10,
                CampaignTotalCount = 0
            })
        };
        using var handler = new RecordingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpSeasonQueryService(http).GetAsync(
            new GetSeasonDetailInput
            {
                SeasonId = 7,
                CampaignPage = 3,
                CampaignPageSize = 10
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.RequestUri!.PathAndQuery
            .ShouldBe("/api/seasons/7?campaignPage=3&campaignPageSize=10");
    }

    /// <summary>Verifies invalid detail paging is rejected before transport.</summary>
    [Fact]
    public async Task GetAsyncReturnsValidationProblemForInvalidInputAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var handler = new RecordingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpSeasonQueryService(http).GetAsync(
            new GetSeasonDetailInput { SeasonId = 7, CampaignPageSize = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.LastRequest.ShouldBeNull();
    }

    /// <summary>Verifies detail responses must identify the exact effective requested campaign paging values.</summary>
    /// <param name="responsePage">The campaign page returned by the malformed response.</param>
    /// <param name="responsePageSize">The campaign page size returned by the malformed response.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    public async Task GetAsyncReturnsServerErrorWhenResponsePagingDoesNotMatchRequestAsync(
        int responsePage,
        int responsePageSize)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonDetailResult
            {
                Season = new SeasonSummary
                {
                    SeasonId = 7,
                    Name = "Season",
                    StartDate = new DateOnly(2026, 1, 1),
                    IsCurrent = true,
                    ConcurrencyToken = Guid.NewGuid()
                },
                Campaigns = [],
                CampaignPage = responsePage,
                CampaignPageSize = responsePageSize,
                CampaignTotalCount = 0
            })
        };
        using var httpHandler = new RecordingHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonQueryService(http).GetAsync(
            new GetSeasonDetailInput
            {
                SeasonId = 7,
                CampaignPage = 2,
                CampaignPageSize = 10
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies a campaign total may trail the detail page under concurrent inserts.</summary>
    [Fact]
    public async Task GetAsyncAcceptsDetailWhenEventuallyConsistentTotalIsSmallerAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonDetailResult
            {
                Season = new SeasonSummary
                {
                    SeasonId = 7,
                    Name = "Season",
                    StartDate = new DateOnly(2026, 1, 1),
                    IsCurrent = false,
                    ConcurrencyToken = Guid.NewGuid()
                },
                Campaigns =
                [
                    new SeasonCampaignSummary
                    {
                        CampaignId = 11,
                        Name = "Campaign",
                        Status = Nova.SharedKernel.Enums.CampaignStatus.Closed,
                        StartDate = new DateOnly(2026, 2, 1),
                        EndDate = new DateOnly(2026, 3, 1),
                        ParticipantCount = 3
                    }
                ],
                CampaignPage = 1,
                CampaignPageSize = 20,
                CampaignTotalCount = 0
            })
        };
        using var httpHandler = new RecordingHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonQueryService(http).GetAsync(
            new GetSeasonDetailInput { SeasonId = 7 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Campaigns.Count.ShouldBe(1);
        result.Value.CampaignTotalCount.ShouldBe(0);
    }

    /// <summary>Verifies detail responses reject campaign statuses outside the public lifecycle enum.</summary>
    [Fact]
    public async Task GetAsyncReturnsServerErrorWhenCampaignStatusIsUndefinedAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonDetailResult
            {
                Season = NewSeasonSummary(7, new DateOnly(2026, 1, 1), isCurrent: false),
                Campaigns =
                [
                    new SeasonCampaignSummary
                    {
                        CampaignId = 11,
                        Name = "Campaign",
                        Status = (Nova.SharedKernel.Enums.CampaignStatus)999,
                        StartDate = new DateOnly(2026, 2, 1),
                        ParticipantCount = 0
                    }
                ],
                CampaignPage = 1,
                CampaignPageSize = 20,
                CampaignTotalCount = 1
            })
        };
        using var httpHandler = new RecordingHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonQueryService(http).GetAsync(
            new GetSeasonDetailInput { SeasonId = 7 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies an eventually consistent campaign total must still be nonnegative.</summary>
    [Fact]
    public async Task GetAsyncReturnsServerErrorWhenCampaignTotalIsNegativeAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonDetailResult
            {
                Season = new SeasonSummary
                {
                    SeasonId = 7,
                    Name = "Season",
                    StartDate = new DateOnly(2026, 1, 1),
                    IsCurrent = false,
                    ConcurrencyToken = Guid.NewGuid()
                },
                Campaigns = [],
                CampaignPage = 1,
                CampaignPageSize = 20,
                CampaignTotalCount = -1
            })
        };
        using var httpHandler = new RecordingHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonQueryService(http).GetAsync(
            new GetSeasonDetailInput { SeasonId = 7 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies campaign rows obey start-date and identifier descending keys.</summary>
    /// <param name="useIdentifierTieBreak">Whether the malformed pair shares a start date.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsyncReturnsServerErrorWhenCampaignOrderIsAscendingAsync(bool useIdentifierTieBreak)
    {
        var firstStartDate = new DateOnly(2026, 1, 1);
        var secondStartDate = useIdentifierTieBreak
            ? firstStartDate
            : new DateOnly(2026, 2, 1);
        await AssertInvalidCampaignOrderAsync(
        [
            NewCampaignSummary(10, firstStartDate),
            NewCampaignSummary(11, secondStartDate)
        ]);
    }

    /// <summary>Asserts the list client rejects a malformed season order.</summary>
    private static async Task AssertInvalidSeasonOrderAsync(
        IReadOnlyList<SeasonSummary> items,
        int page = 1)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonPageResult
            {
                Items = items,
                Page = page,
                PageSize = 20,
                TotalCount = items.Count
            })
        };
        using var httpHandler = new RecordingHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonQueryService(http).ListAsync(
            new GetSeasonListInput { Page = page },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Asserts the detail client rejects a malformed campaign order.</summary>
    private static async Task AssertInvalidCampaignOrderAsync(IReadOnlyList<SeasonCampaignSummary> campaigns)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonDetailResult
            {
                Season = NewSeasonSummary(7, new DateOnly(2026, 1, 1), isCurrent: false),
                Campaigns = campaigns,
                CampaignPage = 1,
                CampaignPageSize = 20,
                CampaignTotalCount = campaigns.Count
            })
        };
        using var httpHandler = new RecordingHandler(response);
        using var http = new HttpClient(httpHandler, disposeHandler: false)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonQueryService(http).GetAsync(
            new GetSeasonDetailInput { SeasonId = 7 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Creates a structurally valid season summary.</summary>
    private static SeasonSummary NewSeasonSummary(long seasonId, DateOnly startDate, bool isCurrent)
        => new()
        {
            SeasonId = seasonId,
            Name = $"Season {seasonId}",
            StartDate = startDate,
            IsCurrent = isCurrent,
            ConcurrencyToken = Guid.NewGuid()
        };

    /// <summary>Creates a structurally valid campaign summary.</summary>
    private static SeasonCampaignSummary NewCampaignSummary(long campaignId, DateOnly startDate)
        => new()
        {
            CampaignId = campaignId,
            Name = $"Campaign {campaignId}",
            Status = Nova.SharedKernel.Enums.CampaignStatus.Closed,
            StartDate = startDate,
            ParticipantCount = 0
        };

    /// <summary>Records the request while returning a fixed response.</summary>
    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        /// <summary>Gets the last request.</summary>
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
}
