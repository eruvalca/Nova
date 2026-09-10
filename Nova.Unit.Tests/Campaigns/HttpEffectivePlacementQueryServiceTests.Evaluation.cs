using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class HttpEffectivePlacementQueryServiceTests
{
    [Theory]
    [InlineData(1, "asc")]
    [InlineData(1, "desc")]
    [InlineData(2, "asc")]
    [InlineData(2, "desc")]
    public async Task EvaluationDiscoveryEmitsRelevanceAndAcceptsExactTryoutBeforeNameMatchesAsync(int endpoint, string direction)
    {
        var payload = TwoRowPayload(endpoint);
        var first = Row(payload, endpoint);
        first["tryoutNumber"] = 42;
        first["lastName"] = "Zulu";
        var second = payload["participants"]!["items"]![1]!;
        second["tryoutNumber"] = 43;
        second["lastName"] = "Adams42";
        CampaignRosterDiscoveryInput input = endpoint == 1
            ? new GetCampaignEffectivePlacementsInput { CampaignId = 42, Search = "42", SortBy = "searchRelevance", SortDirection = direction }
            : new GetClosedCampaignRosterInput { CampaignId = 42, Search = "42", SortBy = "searchRelevance", SortDirection = direction };
        using var handler = new RecordingHandler(request =>
        {
            request.RequestUri!.Query.ShouldContain("sortBy=searchRelevance");
            request.RequestUri.Query.ShouldContain("search=42");
            return Response(payload.ToJsonString());
        });
        using var http = CreateHttp(handler);
        var service = new HttpEffectivePlacementQueryService(http);
        var ordered = await ReadDiscoveryAsync(service, input);
        ordered.IsSuccess.ShouldBeTrue();
        ReverseRows(payload);
        var reversed = await ReadDiscoveryAsync(service, input);
        reversed.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }
}
