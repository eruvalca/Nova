using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nova.Client.Services.Players;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

public sealed partial class HttpPlayerManagementServiceTests
{
    /// <summary>Malformed validation and conflicting receipt markers are protocol failures, not settlement proof.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("")]
    [InlineData("{not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"errors\":{}}")]
    [InlineData("{\"errors\":{\"FirstName\":null}}")]
    [InlineData("{\"errors\":{\"FirstName\":[]}}")]
    [InlineData("{\"errors\":{\"FirstName\":[null]}}")]
    [InlineData("{\"errors\":{\"FirstName\":[\" \"]}}")]
    [InlineData("{\"errors\":{\"FirstName\":[\"Invalid\"]},\"playerCreationReason\":\"possibleDuplicate\"}")]
    [InlineData("{\"errors\":{\"FirstName\":[\"Invalid\"]},\"playerCreationNotCommittedOperationId\":null}")]
    [InlineData("{\"errors\":{\"FirstName\":[\"Invalid\"]},\"playerCreationDuplicate\":{}}")]
    public async Task CreateRejectsMalformedValidationEvidenceAsync(string body)
    {
        var input = CreateInput();
        using var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerManagementService(http).CreateAsync(input, TestContext.Current.CancellationToken);

        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        PlayerCreationProblems.IsNotCommitted(result.Problem, input.OperationId).ShouldBeFalse();
    }

    /// <summary>Well-formed field feedback and correlation remain available without claiming durable rejection.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(HttpStatusCode.BadRequest, "FirstName")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "FirstName")]
    [InlineData(HttpStatusCode.BadRequest, "OperationId")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "OperationId")]
    public async Task CreatePreservesValidValidationFeedbackAsync(HttpStatusCode status, string field)
    {
        var input = CreateInput();
        using var response = new HttpResponseMessage(status)
        {
            Content = JsonContent.Create(new
            {
                errors = new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = ["Invalid value"] },
                traceId = "trace-279"
            })
        };
        using var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerManagementService(http).CreateAsync(input, TestContext.Current.CancellationToken);

        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors![field].ShouldBe(["Invalid value"]);
        result.Problem.Extensions!["traceId"]!.ToString().ShouldBe("trace-279");
        PlayerCreationProblems.IsNotCommitted(result.Problem, input.OperationId).ShouldBeFalse();
    }

    /// <summary>Contradictory rejection evidence cannot release an uncertain pending operation.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("expired")]
    [InlineData("operationMismatch")]
    [InlineData("wrongOperation")]
    [InlineData("missingDuplicate")]
    [InlineData("invalidDuplicate")]
    [InlineData("missingReason")]
    [InlineData("structuredErrors")]
    public async Task CreateRejectsContradictoryConflictEvidenceAsync(string defect)
    {
        var input = CreateInput();
        var body = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["status"] = 409,
            ["detail"] = "Conflict",
            [PlayerCreationProblems.ReasonExtension] = defect is "expired" or "operationMismatch" ? defect : "possibleDuplicate",
            [PlayerCreationProblems.NotCommittedExtension] = defect is "wrongOperation" ? Guid.CreateVersion7() : input.OperationId,
            [PlayerCreationProblems.DuplicateExtension] = new PlayerCreationDuplicate
            {
                PlayerId = defect is "invalidDuplicate" ? 0 : 7,
                LifecycleStatus = Nova.SharedKernel.Enums.LifecycleStatus.Active
            }
        };
        if (defect is "missingDuplicate") { body.Remove(PlayerCreationProblems.DuplicateExtension); }
        if (defect is "missingReason") { body.Remove(PlayerCreationProblems.ReasonExtension); }
        if (defect is "structuredErrors")
        {
            body["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["FirstName"] = ["Invalid value"] };
        }
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(body) };
        using var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpPlayerManagementService(http).CreateAsync(input, TestContext.Current.CancellationToken);
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        PlayerCreationProblems.IsNotCommitted(result.Problem, input.OperationId).ShouldBeFalse();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("operationId")]
    [InlineData("player")]
    [InlineData("completedAt")]
    [InlineData("recoveryExpiresAt")]
    [InlineData("enrollment")]
    [InlineData("gender")]
    [InlineData("jerseyNumber")]
    public async Task CreateRequiresEveryReceiptFieldIncludingNullableEnrollmentAsync(string field)
    {
        var input = CreateInput() with { Gender = null, JerseyNumber = null };
        var node = JsonSerializer.SerializeToNode(CreateCompletion(input, CreatePlayer() with { Gender = null, JerseyNumber = null }), JsonSerializerOptions.Web)!.AsObject();
        var owner = field is "gender" or "jerseyNumber" ? node["player"]!.AsObject() : node;
        owner.Remove(field).ShouldBeTrue();
        using var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
        using var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        (await new HttpPlayerManagementService(http).CreateAsync(input, TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("operation")]
    [InlineData("club")]
    [InlineData("profile")]
    [InlineData("nullPlayer")]
    [InlineData("deadline")]
    [InlineData("completion")]
    [InlineData("campaignId")]
    [InlineData("campaignName")]
    [InlineData("participationId")]
    public async Task CreateRejectsContradictoryReceiptEvidenceAsync(string defect)
    {
        var input = CreateInput();
        var completion = CreateCompletion(input, CreatePlayer());
        completion = defect switch
        {
            "operation" => completion with { OperationId = Guid.CreateVersion7() },
            "club" => completion with { Player = completion.Player with { ClubId = 99 } },
            "profile" => completion with { Player = completion.Player with { LastName = "Changed" } },
            "nullPlayer" => completion with { Player = null! },
            "deadline" => completion with { RecoveryExpiresAt = completion.RecoveryExpiresAt.AddSeconds(1) },
            "completion" => completion with { CompletedAt = completion.RecoveryExpiresAt },
            _ => completion with
            {
                Enrollment = new PlayerCreationEnrollment
                {
                    CampaignId = defect is "campaignId" ? 0 : 4,
                    CampaignName = defect is "campaignName" ? " " : "Tryouts",
                    PlayerCampaignAssignmentId = defect is "participationId" ? 0 : 8
                }
            }
        };
        using var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(completion) };
        using var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        (await new HttpPlayerManagementService(http).CreateAsync(input, TestContext.Current.CancellationToken)).Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task CreateAcceptsPopulatedEnrollmentReceiptAsync()
    {
        var input = CreateInput();
        var completion = CreateCompletion(input, CreatePlayer()) with
        {
            Enrollment = new PlayerCreationEnrollment { CampaignId = 4, CampaignName = "Tryouts", PlayerCampaignAssignmentId = 8 }
        };
        using var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(completion) };
        using var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        (await new HttpPlayerManagementService(http).CreateAsync(input, TestContext.Current.CancellationToken)).Value.ShouldBe(completion);
    }
}
