using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class CampaignEvaluationEndpointUrlTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, null)]
    [InlineData(1, null)]
    [InlineData(null, 5L)]
    [InlineData(1, 0L)]
    [InlineData(1, -1L)]
    [InlineData(0, 5L)]
    [InlineData(-1, 5L)]
    public void HistoryBuildersOmitTheWholeInvalidOrIncompleteCursor(int? daysAfterEpoch, long? beforeId)
    {
        var input = new GetEvaluationHistoryInput
        {
            CampaignId = 12,
            PlayerCampaignAssignmentId = 34,
            BeforeCreatedAt = daysAfterEpoch is { } days ? DateTimeOffset.UnixEpoch.AddDays(days) : null,
            BeforeId = beforeId
        };

        CampaignEndpoints.EvaluationNotesUrl(input).ShouldBe("/api/campaigns/12/participants/34/notes");
        CampaignEndpoints.EvaluationApplicationsUrl(input).ShouldBe("/api/campaigns/12/participants/34/applications");
    }

    [Fact]
    public void HistoryBuildersPreserveTheExclusiveCursorAndEscapeItsOffset()
    {
        var input = new GetEvaluationHistoryInput
        {
            CampaignId = 12,
            PlayerCampaignAssignmentId = 34,
            BeforeCreatedAt = new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.FromMinutes(330)),
            BeforeId = 5
        };
        const string Cursor = "?beforeCreatedAt=2026-09-11T09%3A00%3A00.0000000%2B05%3A30&beforeId=5";

        CampaignEndpoints.EvaluationNotesUrl(input).ShouldBe("/api/campaigns/12/participants/34/notes" + Cursor);
        CampaignEndpoints.EvaluationApplicationsUrl(input).ShouldBe("/api/campaigns/12/participants/34/applications" + Cursor);
    }
}
