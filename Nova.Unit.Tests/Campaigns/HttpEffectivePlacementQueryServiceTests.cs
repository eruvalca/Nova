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

public sealed partial class HttpEffectivePlacementQueryServiceTests
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
            row["localTeam"] = null;
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
        row["localTeam"] = local ? Source(payload, 1)["team"]?.DeepClone() : null;
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
                page["totalCount"] = 2;
                if (endpoint == 1) { payload["counts"] = JsonSerializer.SerializeToNode(new EffectivePlacementCounts(0, 2, 0, 0), JsonSerializerOptions.Web); }
                if (endpoint == 2) { payload["participantCount"] = 2; }
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
    public async Task PageAcceptsDatabaseNameCollationWithConsistentSnapshotTotalsAsync(int endpoint)
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
            payload["counts"] = JsonSerializer.SerializeToNode(new EffectivePlacementCounts(0, 2, 0, 0), JsonSerializerOptions.Web);
        }
        payload[PageName(endpoint)]!["items"]!.AsArray().Add(second);
        payload[PageName(endpoint)]!["totalCount"] = 2;
        if (endpoint == 2)
        {
            payload["participantCount"] = 2;
        }

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
        payload["participants"]!["totalCount"] = 2;
        payload["counts"] = JsonSerializer.SerializeToNode(new EffectivePlacementCounts(0, 2, 0, 0), JsonSerializerOptions.Web);
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
        row["localTeam"] = null;
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

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DiscoveryUsesRepeatedArraysAndExplicitLocalFilterNamesAsync(int endpoint)
    {
        string? path = null;
        using var handler = new RecordingHandler(request =>
        {
            path = request.RequestUri!.PathAndQuery;
            return Response("{}", HttpStatusCode.NotFound);
        });
        using var http = CreateHttp(handler);
        CampaignRosterDiscoveryInput input = endpoint == 1
            ? new GetCampaignEffectivePlacementsInput { CampaignId = 42, TeamId = 60 }
            : new GetClosedCampaignRosterInput { CampaignId = 42 };
        input = input with
        {
            Search = "A&B %_\\/#?",
            GraduationYears = [2028, 2029],
            TagDefinitionIds = [81, 82],
            LocalOutcome = "assigned",
            LocalTeamId = 60,
            ParticipantId = 101,
            SortBy = "displayName",
            SortDirection = "desc",
            Page = 2,
            PageSize = 10
        };

        await ReadDiscoveryAsync(new HttpEffectivePlacementQueryService(http), input);

        var route = endpoint == 1 ? "effective-placements" : "closed-roster";
        var effectiveTeam = endpoint == 1 ? "&teamId=60" : string.Empty;
        path.ShouldBe($"/api/campaigns/42/{route}?page=2&pageSize=10{effectiveTeam}&search=A%26B%20%25_%5C%2F%23%3F&graduationYears=2028&graduationYears=2029&tagDefinitionIds=81&tagDefinitionIds=82&localOutcome=assigned&localTeamId=60&participantId=101&sortBy=displayName&sortDirection=desc");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DiscoveryRejectsMissingNullMalformedOrDuplicateTagEvidenceAsync(int endpoint)
    {
        foreach (var defect in new[] { "missing", "null", "nullRow", "id", "name", "color", "duplicate" })
        {
            var payload = Payload(endpoint);
            var row = Row(payload, endpoint);
            row["appliedTags"] = JsonSerializer.SerializeToNode(new[] { new CampaignParticipantTagSummaryDto(81, "Captain", "primary", false) }, JsonSerializerOptions.Web);
            if (string.Equals(defect, "missing", StringComparison.Ordinal)) { row.Remove("appliedTags"); }
            else if (string.Equals(defect, "null", StringComparison.Ordinal)) { row["appliedTags"] = null; }
            else if (string.Equals(defect, "nullRow", StringComparison.Ordinal)) { row["appliedTags"]![0] = null; }
            else if (string.Equals(defect, "duplicate", StringComparison.Ordinal)) { row["appliedTags"]!.AsArray().Add(row["appliedTags"]![0]!.DeepClone()); }
            else
            {
                var field = defect switch { "id" => "playerTagId", "name" => "tagName", _ => "tagColor" };
                row["appliedTags"]![0]![field] = string.Equals(defect, "id", StringComparison.Ordinal) ? JsonValue.Create(0) : JsonValue.Create(" ");
            }

            (await ReadPayloadAsync(endpoint, payload.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError, defect);
        }
    }

    [Fact]
    public async Task DiscoveryRequiresLocalTeamEvidenceAndUnfilteredClosedScaleAsync()
    {
        foreach (var defect in new[] { "missing", "null", "different" })
        {
            var working = Payload(1);
            if (string.Equals(defect, "missing", StringComparison.Ordinal)) { Row(working, 1).Remove("localTeam"); }
            else if (string.Equals(defect, "null", StringComparison.Ordinal)) { Row(working, 1)["localTeam"] = null; }
            else { Row(working, 1)["localTeam"]!["teamId"] = 999; }
            (await ReadPayloadAsync(1, working.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError, defect);
        }
        foreach (var count in new int?[] { null, -1, 0 })
        {
            var closed = Payload(2);
            if (count is null) { closed.AsObject().Remove("participantCount"); }
            else { closed["participantCount"] = count; }
            (await ReadPayloadAsync(2, closed.ToJsonString())).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }
        var filtered = Payload(2);
        filtered["participantCount"] = 100;
        (await ReadPayloadAsync(2, filtered.ToJsonString())).Value.ShouldBeOfType<ClosedCampaignRosterResult>().ParticipantCount.ShouldBe(100);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DiscoveryVerifiesEachRequestedFilterAgainstReturnedRowsAsync(int endpoint)
    {
        CampaignRosterDiscoveryInput valid = endpoint == 1
            ? new GetCampaignEffectivePlacementsInput { CampaignId = 42 }
            : new GetClosedCampaignRosterInput { CampaignId = 42 };
        valid = valid with { GraduationYears = [2028, 2029], TagDefinitionIds = [81, 82], LocalTeamId = 60, LocalOutcome = "assigned", ParticipantId = 101 };
        var payload = Payload(endpoint);
        Row(payload, endpoint)["appliedTags"] = JsonSerializer.SerializeToNode(new[] { new CampaignParticipantTagSummaryDto(82, "Captain", "primary", false) }, JsonSerializerOptions.Web);
        using var handler = new RecordingHandler(_ => Response(payload.ToJsonString()));
        using var http = CreateHttp(handler);
        var service = new HttpEffectivePlacementQueryService(http);
        (await ReadDiscoveryAsync(service, valid)).IsSuccess.ShouldBeTrue();
        foreach (var invalid in new[]
        {
            valid with { GraduationYears = [2030] }, valid with { TagDefinitionIds = [83] },
            valid with { LocalTeamId = 61 }, valid with { LocalOutcome = "undecided" }, valid with { ParticipantId = 102 }
        })
        {
            (await ReadDiscoveryAsync(service, invalid)).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("graduationYear", "asc")]
    [InlineData("graduationYear", "desc")]
    [InlineData("tryoutNumber", "asc")]
    [InlineData("tryoutNumber", "desc")]
    [InlineData("outcome", "asc")]
    [InlineData("outcome", "desc")]
    [InlineData("assignmentId", "asc")]
    [InlineData("assignmentId", "desc")]
    public async Task DiscoveryAcceptsRequestedNumericSortAndRejectsItsReverseAsync(string sort, string direction)
    {
        foreach (var endpoint in new[] { 1, 2 })
        {
            var payload = TwoRowPayload(endpoint);
            var second = payload["participants"]!["items"]![1]!;
            if (string.Equals(sort, "graduationYear", StringComparison.Ordinal)) { second["graduationYear"] = 2029; }
            if (string.Equals(sort, "tryoutNumber", StringComparison.Ordinal)) { second["tryoutNumber"] = 43; }
            if (string.Equals(sort, "outcome", StringComparison.Ordinal))
            {
                second[SourceName(endpoint)]!["decision"]!["outcome"] = (int)PlacementOutcome.NotSelected;
                second[SourceName(endpoint)]!["decision"]!["teamId"] = null;
                second[SourceName(endpoint)]!["team"] = null;
                if (endpoint == 1)
                {
                    second["localDecision"] = second[SourceName(endpoint)]!["decision"]!.DeepClone();
                    second["localTeam"] = null;
                    second["effectiveTeam"] = null;
                    second["eligibility"] = (int)EffectivePlacementEligibility.Resolved;
                    payload["counts"] = JsonSerializer.SerializeToNode(new EffectivePlacementCounts(0, 1, 1, 0), JsonSerializerOptions.Web);
                }
            }
            if (string.Equals(direction, "desc", StringComparison.Ordinal)) { ReverseRows(payload); }
            CampaignRosterDiscoveryInput input = endpoint == 1
                ? new GetCampaignEffectivePlacementsInput { CampaignId = 42, SortBy = sort, SortDirection = direction }
                : new GetClosedCampaignRosterInput { CampaignId = 42, SortBy = sort, SortDirection = direction };
            using var handler = new RecordingHandler(_ => Response(payload.ToJsonString()));
            using var http = CreateHttp(handler);
            var service = new HttpEffectivePlacementQueryService(http);
            (await ReadDiscoveryAsync(service, input)).IsSuccess.ShouldBeTrue($"{endpoint}/{sort}/{direction}");
            ReverseRows(payload);
            (await ReadDiscoveryAsync(service, input)).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("displayName", "asc")]
    [InlineData("displayName", "desc")]
    [InlineData("teamName", "asc")]
    [InlineData("teamName", "desc")]
    public async Task DiscoveryTextSortEnforcesAscendingIdentityTiesInEitherDirectionAsync(string sort, string direction)
    {
        foreach (var endpoint in new[] { 1, 2 })
        {
            var payload = TwoRowPayload(endpoint);
            CampaignRosterDiscoveryInput input = endpoint == 1
                ? new GetCampaignEffectivePlacementsInput { CampaignId = 42, SortBy = sort, SortDirection = direction }
                : new GetClosedCampaignRosterInput { CampaignId = 42, SortBy = sort, SortDirection = direction };
            using var handler = new RecordingHandler(_ => Response(payload.ToJsonString()));
            using var http = CreateHttp(handler);
            var service = new HttpEffectivePlacementQueryService(http);
            (await ReadDiscoveryAsync(service, input)).IsSuccess.ShouldBeTrue();
            ReverseRows(payload);
            (await ReadDiscoveryAsync(service, input)).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
            var row = Row(payload, endpoint);
            if (string.Equals(sort, "displayName", StringComparison.Ordinal)) { row["lastName"] = "Öberg"; }
            else
            {
                row[SourceName(endpoint)]!["team"]!["teamName"] = "Öberg";
                if (endpoint == 1)
                {
                    row["localTeam"]!["teamName"] = "Öberg";
                    row["effectiveTeam"]!["teamName"] = "Öberg";
                }
            }
            (await ReadDiscoveryAsync(service, input)).IsSuccess.ShouldBeTrue("Distinct text must retain database collation without a browser-side approximation.");
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1, "asc")]
    [InlineData(1, "desc")]
    [InlineData(2, "asc")]
    [InlineData(2, "desc")]
    public async Task DirectionOnlyDiscoveryValidatesAssignmentTiesWithoutApplyingLegacyOrTextCollationOrderAsync(int endpoint, string direction)
    {
        var payload = TwoRowPayload(endpoint);
        var second = payload["participants"]!["items"]![1]!;
        second["playerId"] = 201;
        second[SourceName(endpoint)]!["decision"]!["playerId"] = 201;
        if (endpoint == 1) { second["localDecision"]!["playerId"] = 201; }
        CampaignRosterDiscoveryInput input = endpoint == 1
            ? new GetCampaignEffectivePlacementsInput { CampaignId = 42, SortDirection = direction }
            : new GetClosedCampaignRosterInput { CampaignId = 42, SortDirection = direction };
        string? path = null;
        using var handler = new RecordingHandler(request =>
        {
            path = request.RequestUri!.PathAndQuery;
            return Response(payload.ToJsonString());
        });
        using var http = CreateHttp(handler);
        var service = new HttpEffectivePlacementQueryService(http);

        (await ReadDiscoveryAsync(service, input)).IsSuccess.ShouldBeTrue("Direction-only name ties use assignment IDs, not player IDs.");
        var route = endpoint == 1 ? "effective-placements" : "closed-roster";
        path.ShouldBe($"/api/campaigns/42/{route}?page=1&pageSize=50&sortDirection={direction}");
        (await ReadAsync(service, endpoint)).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        ReverseRows(payload);
        (await ReadDiscoveryAsync(service, input)).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        (await ReadAsync(service, endpoint)).IsSuccess.ShouldBeTrue("Omitting both sort properties retains the original player-ID tie order.");

        Row(payload, endpoint)["graduationYear"] = 2029;
        Row(payload, endpoint)["lastName"] = "Öberg";
        (await ReadDiscoveryAsync(service, input)).IsSuccess.ShouldBeTrue("Distinct text retains database collation regardless of graduation year.");
        ReverseRows(payload);
        (await ReadDiscoveryAsync(service, input)).IsSuccess.ShouldBeTrue("The client must not approximate database string ordering in either direction.");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidDiscoveryIsRejectedBeforeSendingAnyRequestAsync(int endpoint)
    {
        var requests = 0;
        using var handler = new RecordingHandler(_ => { requests++; return Response("{}"); });
        using var http = CreateHttp(handler);
        var service = new HttpEffectivePlacementQueryService(http);
        CampaignRosterDiscoveryInput valid = endpoint == 1
            ? new GetCampaignEffectivePlacementsInput { CampaignId = 42 }
            : new GetClosedCampaignRosterInput { CampaignId = 42 };
        foreach (var invalid in new[]
        {
            valid with { GraduationYears = [2030, 0] }, valid with { TagDefinitionIds = [81, -1] },
            valid with { LocalTeamId = 0 }, valid with { ParticipantId = 0 }, valid with { LocalOutcome = "unknown" },
            valid with { SortBy = "unknown" }, valid with { SortDirection = "sideways" }, valid with { PageSize = 101 }
        })
        {
            (await ReadDiscoveryAsync(service, invalid)).Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        }
        requests.ShouldBe(0);
    }

    private static JsonNode TwoRowPayload(int endpoint)
    {
        var payload = Payload(endpoint);
        var second = Row(payload, endpoint).DeepClone();
        second["playerId"] = 203;
        second["playerCampaignAssignmentId"] = 102;
        second[SourceName(endpoint)]!["decision"]!["playerId"] = 203;
        second[SourceName(endpoint)]!["decision"]!["playerCampaignAssignmentId"] = 102;
        if (endpoint == 1)
        {
            second["localDecision"]!["playerId"] = 203;
            second["localDecision"]!["playerCampaignAssignmentId"] = 102;
            payload["counts"] = JsonSerializer.SerializeToNode(new EffectivePlacementCounts(0, 2, 0, 0), JsonSerializerOptions.Web);
        }
        else { payload["participantCount"] = 2; }
        payload["participants"]!["items"]!.AsArray().Add(second);
        payload["participants"]!["totalCount"] = 2;
        return payload;
    }

    private static void ReverseRows(JsonNode payload)
        => payload["participants"]!["items"] = new JsonArray(payload["participants"]!["items"]!.AsArray().Reverse().Select(row => row!.DeepClone()).ToArray());

    private static async Task<ServiceResult<object>> ReadDiscoveryAsync(HttpEffectivePlacementQueryService service, CampaignRosterDiscoveryInput input)
        => input switch
        {
            GetCampaignEffectivePlacementsInput working => Widen(await service.GetCampaignEffectivePlacementsAsync(working, TestContext.Current.CancellationToken)),
            GetClosedCampaignRosterInput closed => Widen(await service.GetClosedCampaignRosterAsync(closed, TestContext.Current.CancellationToken)),
            _ => throw new ArgumentOutOfRangeException(nameof(input))
        };

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
                    EffectivePlacementEligibility.OptionalReassignment, PlacementCorrectionReason.None) { LocalTeam = source.Team }], 1, 50, 1)),
            2 => new ClosedCampaignRosterResult(new(42, "Campaign", CampaignStatus.Closed, season),
                new([new(101, 202, "Zoe", "Adams", 2028, 42, source)], 1, 50, 1))
            { ParticipantCount = 1 },
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
