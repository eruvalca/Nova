using Bunit;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignEvaluationPanelTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("notes", "Retry notes")]
    [InlineData("applications", "Retry traits")]
    [InlineData("choices", "Retry choices")]
    public void EvidenceTransportCancellationRemainsRegionalAndRetryable(string region, string retry)
    {
        var failure = new OperationCanceledException("Transport timed out independently of the component.");
        if (string.Equals(region, "notes", StringComparison.Ordinal))
        {
            _evidence.GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>()).Returns(
                Task.FromException<ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>>(failure),
                Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>(new EvaluationHistoryPage<CampaignParticipantNoteDto>([Note()], null))));
        }
        else if (string.Equals(region, "applications", StringComparison.Ordinal))
        {
            _evidence.GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>()).Returns(
                Task.FromException<ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>>(failure),
                Task.FromResult(new ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>(new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>(
                    [new(1, 11, "Strong", "#65743A", false, "Coach Rivera", DateTimeOffset.UtcNow, false)], null))));
        }
        else
        {
            _evidence.GetTagChoicesAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>()).Returns(
                Task.FromException<ServiceResult<IReadOnlyList<EvaluationTagChoice>>>(failure),
                Task.FromResult(new ServiceResult<IReadOnlyList<EvaluationTagChoice>>(new EvaluationTagChoice[] { new(11, "Strong", "#65743A", 1) })));
        }
        var cut = Panel(new() { ParticipantId = 301 });
        cut.WaitForAssertion(() => cut.Find(".evaluation-save").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("#evaluation-player-heading").TextContent.ShouldContain("Jordan Lee");
        Button(cut, "Add a trait").Click();
        cut.Find("#evaluation-note").Input("Unrelated draft remains");
        Button(cut, retry).Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain(retry));
        cut.Find(".evaluation-observations").TextContent.ShouldContain("First observation.");
        cut.Find(".evaluation-traits").TextContent.ShouldContain("Coach Rivera");
        cut.Find(".evaluation-trait-picker").TextContent.ShouldContain("Strong");
        cut.Find("#evaluation-note").GetAttribute("value").ShouldBe("Unrelated draft remains");
        _ = _evidence.Received(string.Equals(region, "notes", StringComparison.Ordinal) ? 2 : 1).GetNotesAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
        _ = _evidence.Received(string.Equals(region, "applications", StringComparison.Ordinal) ? 2 : 1).GetApplicationsAsync(Arg.Any<GetEvaluationHistoryInput>(), Arg.Any<CancellationToken>());
        _ = _evidence.Received(string.Equals(region, "choices", StringComparison.Ordinal) ? 2 : 1).GetTagChoicesAsync(Arg.Any<GetCampaignParticipantDetailInput>(), Arg.Any<CancellationToken>());
    }
}
