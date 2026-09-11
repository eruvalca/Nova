using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>Direct evaluation reads validate before depending on database availability.</summary>
public sealed class CampaignEvaluationQueryValidationTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("notes", 0, 301)]
    [InlineData("notes", -1, 301)]
    [InlineData("notes", 10, 0)]
    [InlineData("notes", 10, -1)]
    [InlineData("applications", 0, 301)]
    [InlineData("applications", -1, 301)]
    [InlineData("applications", 10, 0)]
    [InlineData("applications", 10, -1)]
    [InlineData("choices", 0, 301)]
    [InlineData("choices", -1, 301)]
    [InlineData("choices", 10, 0)]
    [InlineData("choices", 10, -1)]
    public async Task InvalidIdentityReturnsFieldValidationWithoutCreatingContextAsync(string operation, long campaignId, long participantId)
    {
        var factory = FailingFactory();
        var service = new CampaignEvaluationQueryService(factory, Substitute.For<ICurrentUserProvider>());
        var field = campaignId <= 0 ? nameof(GetEvaluationHistoryInput.CampaignId) : nameof(GetEvaluationHistoryInput.PlayerCampaignAssignmentId);

        if (string.Equals(operation, "choices", StringComparison.Ordinal))
        {
            AssertValidation(await service.GetTagChoicesAsync(new()
            {
                CampaignId = campaignId,
                PlayerCampaignAssignmentId = participantId,
            }, TestContext.Current.CancellationToken), field);
        }
        else
        {
            await AssertHistoryValidationAsync(service, operation, new()
            {
                CampaignId = campaignId,
                PlayerCampaignAssignmentId = participantId,
            }, field);
        }

        factory.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("notes", "timestamp-only")]
    [InlineData("notes", "identity-only")]
    [InlineData("notes", "zero-identity")]
    [InlineData("notes", "epoch")]
    [InlineData("applications", "timestamp-only")]
    [InlineData("applications", "identity-only")]
    [InlineData("applications", "zero-identity")]
    [InlineData("applications", "epoch")]
    public async Task InvalidHistoryCursorReturnsStructuredValidationWithoutCreatingContextAsync(string operation, string cursor)
    {
        var factory = FailingFactory();
        var service = new CampaignEvaluationQueryService(factory, Substitute.For<ICurrentUserProvider>());
        var input = new GetEvaluationHistoryInput
        {
            CampaignId = 10,
            PlayerCampaignAssignmentId = 301,
            BeforeCreatedAt = cursor switch { "identity-only" => null, "epoch" => DateTimeOffset.UnixEpoch, _ => DateTimeOffset.UnixEpoch.AddDays(1) },
            BeforeId = cursor switch { "timestamp-only" => null, "zero-identity" => 0, _ => 12 },
        };

        await AssertHistoryValidationAsync(service, operation, input, nameof(GetEvaluationHistoryInput.BeforeId));

        factory.ReceivedCalls().ShouldBeEmpty();
    }

    private static async Task AssertHistoryValidationAsync(CampaignEvaluationQueryService service, string operation, GetEvaluationHistoryInput input, string field)
    {
        if (string.Equals(operation, "notes", StringComparison.Ordinal))
        {
            AssertValidation(await service.GetNotesAsync(input, TestContext.Current.CancellationToken), field);
        }
        else
        {
            AssertValidation(await service.GetApplicationsAsync(input, TestContext.Current.CancellationToken), field);
        }
    }

    private static void AssertValidation<T>(ServiceResult<T> result, string field)
    {
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.StatusCode.ShouldBe(400);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.ShouldContainKey(field);
        result.Problem.Errors[field].ShouldNotBeEmpty();
    }

    private static IDbContextFactory<NovaReadDbContext> FailingFactory()
    {
        var factory = Substitute.For<IDbContextFactory<NovaReadDbContext>>();
        factory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns<Task<NovaReadDbContext>>(_ => throw new InvalidOperationException("Invalid input must not depend on database availability."));
        return factory;
    }
}
