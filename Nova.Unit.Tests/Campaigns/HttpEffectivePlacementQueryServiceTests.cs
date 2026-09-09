using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class HttpEffectivePlacementQueryServiceTests
{
    private static readonly Guid _decisionToken = new("829a9916-05b7-4bfe-896d-ed61c4ed6e18");

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0, "/api/seasons/current/roster?page=1&pageSize=50")]
    [InlineData(1, "/api/campaigns/42/effective-placements?page=1&pageSize=50")]
    [InlineData(2, "/api/campaigns/42/closed-roster?page=1&pageSize=50")]
    public async Task PopulatedResponsesPreserveDecisionEvidenceAndUseSharedRoutesAsync(int endpoint, string expectedPath)
    {
        string? capturedPath = null;
        using var handler = new RecordingHandler(request =>
        {
            request.Method.ShouldBe(HttpMethod.Get);
            capturedPath = request.RequestUri!.PathAndQuery;
            return Response(Payload(endpoint).ToJsonString());
        });
        using var http = CreateHttp(handler);

        var result = await ReadAsync(new HttpEffectivePlacementQueryService(http), endpoint);

        result.IsSuccess.ShouldBeTrue();
        capturedPath.ShouldBe(expectedPath);
        var decision = result.Value switch
        {
            CurrentSeasonRosterResult roster => roster.Roster.Items.Single().Source.Decision,
            CampaignEffectivePlacementsResult work => work.Participants.Items.Single().LocalDecision!,
            ClosedCampaignRosterResult closed => closed.Participants.Items.Single().Source.Decision,
            _ => throw new InvalidOperationException("Unexpected result type.")
        };
        decision.ShouldBe(Decision());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RequiredSuccessBodyRejectsEmptyNullMalformedAndUnexpectedJsonAsync(int endpoint)
    {
        foreach (var payload in new[] { "", "null", "{", "[]", "{}" })
        {
            var result = await ReadPayloadAsync(endpoint, payload);
            result.IsProblem.ShouldBeTrue(payload);
            result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SuccessBodyRejectsNullNestedPagesCollectionsRowsAndDecisionSourcesAsync(int endpoint)
    {
        foreach (var field in new[] { "page", "items", "row", "source", "decision", "name" })
        {
            var payload = Payload(endpoint);
            var pageName = PageName(endpoint);
            switch (field)
            {
                case "page": payload[pageName] = null; break;
                case "items": payload[pageName]!["items"] = null; break;
                case "row": payload[pageName]!["items"]![0] = null; break;
                case "source": Row(payload, endpoint)[SourceName(endpoint)] = null; break;
                case "decision": Source(payload, endpoint)["decision"] = null; break;
                case "name": Row(payload, endpoint)["firstName"] = null; break;
            }

            var result = await ReadPayloadAsync(endpoint, payload.ToJsonString());
            result.IsProblem.ShouldBeTrue(field);
            result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SavedDecisionRejectsMissingOrNullRequiredAttributionAsync(int endpoint)
    {
        foreach (var field in new[] { "recordedAt", "recordedById", "actorDisplayName" })
        {
            foreach (var omit in new[] { false, true })
            {
                var payload = Payload(endpoint);
                var decision = Source(payload, endpoint)["decision"]!.AsObject();
                if (omit)
                {
                    decision.Remove(field).ShouldBeTrue();
                }
                else
                {
                    decision[field] = null;
                }

                var result = await ReadPayloadAsync(endpoint, payload.ToJsonString());
                result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
            }
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("localDecision")]
    [InlineData("effectiveDecision")]
    [InlineData("effectiveTeam")]
    public async Task WorkingResponseRequiresExplicitNullableEvidenceFieldsAsync(string field)
    {
        var payload = Payload(1);
        Row(payload, 1).Remove(field).ShouldBeTrue();
        (await ReadPayloadAsync(1, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task RosterRequiresExplicitSeasonEvenWhenAbsentAsync()
    {
        var payload = Payload(0);
        payload.AsObject().Remove("season").ShouldBeTrue();
        (await ReadPayloadAsync(0, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);

        payload["season"] = null;
        payload["roster"]!["items"] = new JsonArray();
        payload["roster"]!["totalCount"] = 0;
        (await ReadPayloadAsync(0, payload.ToJsonString())).IsSuccess.ShouldBeTrue();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SavedDecisionRejectsInvalidSourceIdentityAndAttributionAsync(int endpoint)
    {
        foreach (var field in new[] { "playerId", "seasonId", "seasonOpeningSequence", "campaignId", "playerCampaignAssignmentId", "recordedById" })
        {
            var payload = Payload(endpoint);
            Source(payload, endpoint)["decision"]![field] = field is "playerId" or "seasonId" ? 999 : 0;
            (await ReadPayloadAsync(endpoint, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }

        foreach (var field in new[] { "concurrencyToken", "actorDisplayName", "recordedAt" })
        {
            var payload = Payload(endpoint);
            Source(payload, endpoint)["decision"]![field] = field switch
            {
                "concurrencyToken" => Guid.Empty.ToString(),
                "recordedAt" => DateTimeOffset.MinValue.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                _ => " "
            };
            (await ReadPayloadAsync(endpoint, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("playerCampaignAssignmentId")]
    [InlineData("playerId")]
    [InlineData("campaignId")]
    [InlineData("concurrencyToken")]
    public async Task WorkingLocalDecisionMustMatchEnrollmentIdentityAndTokenAsync(string field)
    {
        var payload = Payload(1);
        var local = Row(payload, 1)["localDecision"]!;
        local[field] = field is "concurrencyToken" ? JsonValue.Create(Guid.NewGuid()) : JsonValue.Create(999);
        (await ReadPayloadAsync(1, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("campaignId")]
    [InlineData("playerCampaignAssignmentId")]
    public async Task ClosedDecisionMustBelongToRequestedCampaignAndParticipationAsync(string field)
    {
        var payload = Payload(2);
        Source(payload, 2)["decision"]![field] = 999;
        (await ReadPayloadAsync(2, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SourceRejectsMismatchedOrAbsentAssignedTeamAsync(int endpoint)
    {
        foreach (var missing in new[] { false, true })
        {
            var payload = Payload(endpoint);
            var source = Source(payload, endpoint);
            if (missing)
            {
                source["team"] = null;
            }
            else
            {
                source["team"]!["teamId"] = 999;
            }

            (await ReadPayloadAsync(endpoint, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("eligibility")]
    [InlineData("correctionReason")]
    [InlineData("playerLifecycleStatus")]
    [InlineData("effectiveTeam")]
    [InlineData("localMissing")]
    public async Task WorkingResponseRejectsContradictoryEligibilityAndTeamRelationsAsync(string field)
    {
        var payload = Payload(1);
        var row = Row(payload, 1);
        if (field is "effectiveTeam")
        {
            row[field] = null;
        }
        else if (field is "localMissing")
        {
            row["effectiveDecision"]!["decision"]!["campaignId"] = 99;
        }
        else
        {
            row[field] = field is "eligibility" ? (int)EffectivePlacementEligibility.NeedsPlacement : 999;
        }

        (await ReadPayloadAsync(1, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementCorrectionReason.TeamArchived)]
    [InlineData(PlacementCorrectionReason.TeamIncompatible)]
    [InlineData(PlacementCorrectionReason.TeamUnavailable)]
    public async Task WorkingAcceptsUnresolvedInvalidAssignmentWithRetainedEvidenceAsync(PlacementCorrectionReason correction)
    {
        var payload = Payload(1);
        var row = Row(payload, 1);
        row["eligibility"] = (int)EffectivePlacementEligibility.NeedsPlacement;
        row["correctionReason"] = (int)correction;
        row["effectiveTeam"] = null;
        if (correction == PlacementCorrectionReason.TeamUnavailable)
        {
            row["localDecision"]!["teamId"] = null;
            Source(payload, 1)["decision"]!["teamId"] = null;
            Source(payload, 1)["team"] = null;
        }

        (await ReadPayloadAsync(1, payload.ToJsonString())).IsSuccess.ShouldBeTrue();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Assigned, false, EffectivePlacementEligibility.OptionalReassignment)]
    [InlineData(PlacementOutcome.NotSelected, false, EffectivePlacementEligibility.NeedsPlacement)]
    [InlineData(PlacementOutcome.NotSelected, true, EffectivePlacementEligibility.Resolved)]
    [InlineData(PlacementOutcome.Withdrawn, false, EffectivePlacementEligibility.Unavailable)]
    [InlineData(PlacementOutcome.Withdrawn, true, EffectivePlacementEligibility.Unavailable)]
    public async Task WorkingAcceptsDistinctLocalAndPriorDecisionSemanticsAsync(
        PlacementOutcome outcome, bool local, EffectivePlacementEligibility eligibility)
    {
        var payload = Payload(1);
        var row = Row(payload, 1);
        var decision = Source(payload, 1)["decision"]!;
        decision["outcome"] = (int)outcome;
        if (!local)
        {
            decision["campaignId"] = 41;
            decision["playerCampaignAssignmentId"] = 99;
            decision["seasonOpeningSequence"] = 2;
            decision["concurrencyToken"] = Guid.NewGuid();
        }
        if (outcome != PlacementOutcome.Assigned)
        {
            decision["teamId"] = null;
            Source(payload, 1)["team"] = null;
            row["effectiveTeam"] = null;
        }
        row["localDecision"] = local ? decision.DeepClone() : null;
        row["eligibility"] = (int)eligibility;

        (await ReadPayloadAsync(1, payload.ToJsonString())).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task WorkingAcceptsArchivedPlayerWithoutEffectiveMembershipAsync()
    {
        var payload = Payload(1);
        var row = Row(payload, 1);
        row["playerLifecycleStatus"] = (int)LifecycleStatus.Archived;
        row["eligibility"] = (int)EffectivePlacementEligibility.Unavailable;
        row["effectiveTeam"] = null;

        (await ReadPayloadAsync(1, payload.ToJsonString())).IsSuccess.ShouldBeTrue();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PageRejectsDuplicatesWrongBoundsAndPortableTieOrderAsync(int endpoint)
    {
        foreach (var invalid in new[] { "duplicate", "page", "pageSize", "totalCount", "reverseTie" })
        {
            var payload = Payload(endpoint);
            var page = payload[PageName(endpoint)]!;
            if (invalid is "duplicate" or "reverseTie")
            {
                var second = Row(payload, endpoint).DeepClone();
                if (invalid is "reverseTie")
                {
                    second["playerId"] = 201;
                    var source = second[SourceName(endpoint)]!;
                    source["decision"]!["playerId"] = 201;
                    source["decision"]!["playerCampaignAssignmentId"] = 102;
                    if (endpoint != 0)
                    {
                        second["playerCampaignAssignmentId"] = 102;
                    }
                    if (endpoint == 1)
                    {
                        second["localDecision"]!["playerId"] = 201;
                        second["localDecision"]!["playerCampaignAssignmentId"] = 102;
                    }
                }
                page["items"]!.AsArray().Add(second);
            }
            else
            {
                page[invalid] = invalid is "totalCount" ? -1 : 2;
            }

            (await ReadPayloadAsync(endpoint, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PageAcceptsDatabaseNameCollationAndEventuallyConsistentTotalsAsync(int endpoint)
    {
        var payload = Payload(endpoint);
        var second = Row(payload, endpoint).DeepClone();
        second["playerId"] = 203;
        second["lastName"] = "Aardvark";
        second[SourceName(endpoint)]!["decision"]!["playerId"] = 203;
        second[SourceName(endpoint)]!["decision"]!["playerCampaignAssignmentId"] = 102;
        if (endpoint != 0)
        {
            second["playerCampaignAssignmentId"] = 102;
        }
        if (endpoint == 1)
        {
            second["localDecision"]!["playerId"] = 203;
            second["localDecision"]!["playerCampaignAssignmentId"] = 102;
            payload["counts"] = JsonSerializer.SerializeToNode(new EffectivePlacementCounts(0, 0, 0, 0), JsonSerializerOptions.Web);
        }
        payload[PageName(endpoint)]!["items"]!.AsArray().Add(second);
        payload[PageName(endpoint)]!["totalCount"] = endpoint == 2 ? 2 : 0;

        (await ReadPayloadAsync(endpoint, payload.ToJsonString())).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task WorkingPageRejectsDescendingGraduationYearsAsync()
    {
        var payload = Payload(1);
        var second = Row(payload, 1).DeepClone();
        second["playerId"] = 203;
        second["playerCampaignAssignmentId"] = 102;
        second["graduationYear"] = 2027;
        second["localDecision"]!["playerId"] = 203;
        second["localDecision"]!["playerCampaignAssignmentId"] = 102;
        second["effectiveDecision"]!["decision"]!["playerId"] = 203;
        second["effectiveDecision"]!["decision"]!["playerCampaignAssignmentId"] = 102;
        payload["participants"]!["items"]!.AsArray().Add(second);
        (await ReadPayloadAsync(1, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task InvalidInputIsRejectedBeforeAnyHttpRequestAsync()
    {
        var requests = 0;
        using var handler = new RecordingHandler(_ =>
        {
            requests++;
            return Response("{}");
        });
        using var http = CreateHttp(handler);
        var service = new HttpEffectivePlacementQueryService(http);

        (await service.GetCurrentSeasonRosterAsync(new() { PageSize = 101 }, TestContext.Current.CancellationToken))
            .Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        (await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = 0 }, TestContext.Current.CancellationToken))
            .Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        (await service.GetClosedCampaignRosterAsync(new() { CampaignId = 42, Page = 0 }, TestContext.Current.CancellationToken))
            .Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        requests.ShouldBe(0);
    }

    [Fact]
    public async Task SearchAndWorkingFiltersAreEncodedWithoutChangingTheirMeaningAsync()
    {
        var paths = new List<string>();
        using var handler = new RecordingHandler(request =>
        {
            paths.Add(request.RequestUri!.AbsoluteUri);
            return Response("{}", HttpStatusCode.NotFound);
        });
        using var http = CreateHttp(handler);
        var service = new HttpEffectivePlacementQueryService(http);
        const string Search = "A&B %_\\/#?";

        await service.GetCurrentSeasonRosterAsync(new() { TeamId = 60, GraduationYear = 2028, Search = Search }, TestContext.Current.CancellationToken);
        await service.GetCampaignEffectivePlacementsAsync(new()
        {
            CampaignId = 42,
            TeamId = 60,
            GraduationYear = 2028,
            Search = Search,
            Eligibility = "needsplacement",
            Page = 2,
            PageSize = 10
        }, TestContext.Current.CancellationToken);

        paths[0].ShouldBe("https://example.com/api/seasons/current/roster?page=1&pageSize=50&teamId=60&graduationYear=2028&search=A%26B%20%25_%5C%2F%23%3F");
        paths[1].ShouldBe("https://example.com/api/campaigns/42/effective-placements?page=2&pageSize=10&teamId=60&graduationYear=2028&search=A%26B%20%25_%5C%2F%23%3F&eligibility=NeedsPlacement");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task HttpProblemPreservesConflictAndTraceIdentifierAsync(int endpoint)
    {
        var result = await ReadPayloadAsync(endpoint,
            """{"status":409,"title":"Conflict","detail":"Campaign is closed.","traceId":"0123456789abcdef0123456789abcdef"}""",
            HttpStatusCode.Conflict);

        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("Campaign is closed.");
        result.Problem.Extensions!["traceId"]!.ToString().ShouldBe("0123456789abcdef0123456789abcdef");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("needsPlacement")]
    [InlineData("optionalReassignment")]
    [InlineData("resolved")]
    [InlineData("unavailable")]
    public async Task WorkingCountsRequireEverySectionInsteadOfDefaultingMissingValuesAsync(string field)
    {
        var payload = Payload(1);
        payload["counts"]!.AsObject().Remove(field).ShouldBeTrue();
        (await ReadPayloadAsync(1, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("eligibility")]
    [InlineData("correctionReason")]
    [InlineData("playerLifecycleStatus")]
    public async Task WorkingRequiresExplicitEnumsEvenWhenTheirDefaultsWouldBeValidAsync(string field)
    {
        var payload = Payload(1);
        var row = Row(payload, 1);
        row["localDecision"] = null;
        row["effectiveDecision"] = null;
        row["effectiveTeam"] = null;
        row["eligibility"] = (int)EffectivePlacementEligibility.NeedsPlacement;
        row.Remove(field).ShouldBeTrue();
        (await ReadPayloadAsync(1, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PageRequiresExplicitTotalCountInsteadOfDefaultingToZeroAsync(int endpoint)
    {
        var payload = Payload(endpoint);
        payload[PageName(endpoint)]!.AsObject().Remove("totalCount").ShouldBeTrue();
        (await ReadPayloadAsync(endpoint, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task WorkingRequiresExplicitCampaignLifecycleStatusAsync()
    {
        var payload = Payload(1);
        payload["campaign"]!.AsObject().Remove("status").ShouldBeTrue();
        (await ReadPayloadAsync(1, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task SameCampaignEffectiveDecisionCannotHideItsLocalDecisionAsync()
    {
        var payload = Payload(1);
        Row(payload, 1)["localDecision"] = null;
        (await ReadPayloadAsync(1, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task TeamUnavailableCorrectionCannotClaimVisibleSavedTeamAsync()
    {
        var payload = Payload(1);
        var row = Row(payload, 1);
        row["correctionReason"] = (int)PlacementCorrectionReason.TeamUnavailable;
        row["eligibility"] = (int)EffectivePlacementEligibility.NeedsPlacement;
        row["effectiveTeam"] = null;

        (await ReadPayloadAsync(1, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    private static CampaignSavedPlacementDecision Decision() => new(101, 202, 42, 50, 3,
        PlacementOutcome.Assigned, 60, DateTimeOffset.UnixEpoch, 70, "Casey Member", _decisionToken);

    private static JsonNode Payload(int endpoint)
    {
        var source = new PlacementDecisionSource(Decision(), "Campaign", new(60, "Alpha"));
        var season = new PlacementSeasonIdentity(50, "Season");
        object payload = endpoint switch
        {
            0 => new CurrentSeasonRosterResult(season, new([new(202, "Zoe", "Adams", 2028, source)], 1, 50, 1)),
            1 => new CampaignEffectivePlacementsResult(new(42, "Campaign", CampaignStatus.Active, season), new(0, 1, 0, 0),
                new([new(101, 202, "Zoe", "Adams", 2028, 42, LifecycleStatus.Active, _decisionToken, Decision(), source, source.Team,
                    EffectivePlacementEligibility.OptionalReassignment, PlacementCorrectionReason.None)], 1, 50, 1)),
            2 => new ClosedCampaignRosterResult(new(42, "Campaign", CampaignStatus.Closed, season),
                new([new(101, 202, "Zoe", "Adams", 2028, 42, source)], 1, 50, 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
        };
        return JsonSerializer.SerializeToNode(payload, JsonSerializerOptions.Web)!;
    }

    private static string PageName(int endpoint) => endpoint == 0 ? "roster" : "participants";
    private static string SourceName(int endpoint) => endpoint == 1 ? "effectiveDecision" : "source";
    private static JsonObject Row(JsonNode payload, int endpoint) => payload[PageName(endpoint)]!["items"]![0]!.AsObject();
    private static JsonObject Source(JsonNode payload, int endpoint) => Row(payload, endpoint)[SourceName(endpoint)]!.AsObject();
    private static HttpClient CreateHttp(RecordingHandler handler) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://example.com") };
    private static HttpResponseMessage Response(string payload, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };

    private static async Task<ServiceResult<object>> ReadPayloadAsync(int endpoint, string payload, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var handler = new RecordingHandler(_ => Response(payload, status));
        using var http = CreateHttp(handler);
        return await ReadAsync(new HttpEffectivePlacementQueryService(http), endpoint);
    }

    private static async Task<ServiceResult<object>> ReadAsync(HttpEffectivePlacementQueryService service, int endpoint)
        => endpoint switch
        {
            0 => Widen(await service.GetCurrentSeasonRosterAsync(new(), TestContext.Current.CancellationToken)),
            1 => Widen(await service.GetCampaignEffectivePlacementsAsync(new() { CampaignId = 42 }, TestContext.Current.CancellationToken)),
            2 => Widen(await service.GetClosedCampaignRosterAsync(new() { CampaignId = 42 }, TestContext.Current.CancellationToken)),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
        };

    private static ServiceResult<object> Widen<T>(ServiceResult<T> result) where T : class
        => result.Match<ServiceResult<object>>(value => (object)value, problem => problem);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(callback(request));
    }
}
