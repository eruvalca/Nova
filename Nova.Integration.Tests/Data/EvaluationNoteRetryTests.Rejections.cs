using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class EvaluationNoteRetryTests
{
    [Fact]
    public async Task DurableClosedRejectionPreventsDelayedOriginalFromCommittingAfterReopenAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var actor = BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue;
        var (club, campaign, assignment, _) = await SeedNoteDataAsync(actor, Guid.NewGuid().ToString("N"), withNote: false);
        fixture.CurrentUser.UserId = actor;
        fixture.CurrentUser.ClubId = club;
        var gate = new BeforeEvaluationAuthorizationGate();
        var delayedService = new EvaluationNoteService(new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, gate), fixture.CurrentUser, NullLogger<EvaluationNoteService>.Instance);
        var retryService = new EvaluationNoteService(new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser), fixture.CurrentUser, NullLogger<EvaluationNoteService>.Instance);
        var input = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = assignment, Content = "Delayed original evidence" };
        var delayed = delayedService.AddAsync(input, token);
        try
        {
            await gate.Entered.Task.WaitAsync(token);
            delayed.IsCompleted.ShouldBeFalse();
            await SetRejectionCampaignStatusAsync(campaign, actor, CampaignStatus.Closed, token);
            var rejected = await retryService.AddAsync(input, token);
            rejected.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
            EvaluationMutationRejection.IsNotCommitted(rejected.Problem, input.OperationId).ShouldBeTrue();
            await SetRejectionCampaignStatusAsync(campaign, actor, CampaignStatus.Active, token);
            gate.Release.TrySetResult();
            var original = await delayed;
            original.Problem.Kind.ShouldBe(rejected.Problem.Kind);
            original.Problem.Detail.ShouldBe(rejected.Problem.Detail);
            EvaluationMutationRejection.IsNotCommitted(original.Problem, input.OperationId).ShouldBeTrue();
            await using var verify = fixture.CreateAdminContext();
            (await verify.Notes.CountAsync(note => note.PlayerCampaignAssignmentId == assignment, token)).ShouldBe(0);
            (await verify.EvaluationMutationReceipts.CountAsync(receipt => receipt.ClubId == club && receipt.OperationId == input.OperationId, token)).ShouldBe(1);
        }
        finally
        {
            gate.Release.TrySetResult();
            await delayed;
        }
    }

    [Fact]
    public async Task FailedRejectionReceiptCommitCannotPublishNoncommitProofAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var actor = BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue;
        var (club, campaign, assignment, _) = await SeedNoteDataAsync(actor, Guid.NewGuid().ToString("N"), withNote: false);
        fixture.CurrentUser.UserId = actor;
        fixture.CurrentUser.ClubId = club;
        await SetRejectionCampaignStatusAsync(campaign, actor, CampaignStatus.Closed, token);
        var failure = new RejectionCommitFailure();
        var service = new EvaluationNoteService(new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, failure), fixture.CurrentUser, NullLogger<EvaluationNoteService>.Instance);
        var input = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = assignment, Content = "No negative receipt committed" };

        await Should.ThrowAsync<InvalidOperationException>(() => service.AddAsync(input, token));

        failure.Calls.ShouldBe(1);
        await using (var verify = fixture.CreateAdminContext())
        {
            (await verify.Notes.CountAsync(note => note.PlayerCampaignAssignmentId == assignment, token)).ShouldBe(0);
            (await verify.EvaluationMutationReceipts.CountAsync(receipt => receipt.ClubId == club && receipt.OperationId == input.OperationId, token)).ShouldBe(0);
        }
        await SetRejectionCampaignStatusAsync(campaign, actor, CampaignStatus.Active, token);
        var retry = new EvaluationNoteService(new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser), fixture.CurrentUser, NullLogger<EvaluationNoteService>.Instance);
        var result = await retry.AddAsync(input, token);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Receipt.OperationId.ShouldBe(input.OperationId);
    }

    [Fact]
    public async Task NegativeReceiptRecoversAfterLostCommitAcknowledgementAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var actor = BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue;
        var (club, campaign, assignment, _) = await SeedNoteDataAsync(actor, Guid.NewGuid().ToString("N"), withNote: false);
        fixture.CurrentUser.UserId = actor;
        fixture.CurrentUser.ClubId = club;
        await SetRejectionCampaignStatusAsync(campaign, actor, CampaignStatus.Closed, token);
        var failure = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, failure);
        var service = new EvaluationNoteService(factory, fixture.CurrentUser, NullLogger<EvaluationNoteService>.Instance);
        var input = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = assignment, Content = "Negative acknowledgement lost" };

        var result = await service.AddAsync(input, token);

        failure.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBeGreaterThanOrEqualTo(3);
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        EvaluationMutationRejection.IsNotCommitted(result.Problem, input.OperationId).ShouldBeTrue();
        await SetRejectionCampaignStatusAsync(campaign, actor, CampaignStatus.Active, token);
        var replay = await service.AddAsync(input, token);
        replay.Problem.Detail.ShouldBe(result.Problem.Detail);
        EvaluationMutationRejection.IsNotCommitted(replay.Problem, input.OperationId).ShouldBeTrue();
        await using var verify = fixture.CreateAdminContext();
        (await verify.Notes.CountAsync(note => note.PlayerCampaignAssignmentId == assignment, token)).ShouldBe(0);
        (await verify.EvaluationMutationReceipts.CountAsync(receipt => receipt.ClubId == club && receipt.OperationId == input.OperationId, token)).ShouldBe(1);
    }

    [Fact]
    public async Task RejectedMutationRollsBackFlushedEffectsBeforeCommittingNegativeReceiptAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var actor = BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue;
        var (club, _, assignment, _) = await SeedNoteDataAsync(actor, Guid.NewGuid().ToString("N"), withNote: false);
        fixture.CurrentUser.UserId = actor;
        fixture.CurrentUser.ClubId = club;
        var executor = new EvaluationMutationExecutor(new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser), fixture.CurrentUser);
        var input = new AddEvaluationNoteInput { OperationId = Guid.CreateVersion7(), PlayerCampaignAssignmentId = assignment, Content = "Rolled back before definitive rejection" };
        var calls = 0;
        async Task<ServiceResult<EvaluationNoteMutationSuccess>> RejectAfterWriteAsync(Nova.Data.NovaDbContext db, long actorId, long clubId, DateTimeOffset expiresAt)
        {
            calls++;
            db.Notes.Add(new Nova.Entities.NoteEntity
            {
                AuthorDisplayName = "Retry Member",
                CreationOperationId = input.OperationId,
                Content = input.Content,
                PlayerCampaignAssignmentId = assignment,
                ClubId = clubId,
                CreatedById = actorId
            });
            await db.SaveChangesAsync(token);
            (await db.Notes.CountAsync(note => note.CreationOperationId == input.OperationId, token)).ShouldBe(1);
            return ServiceProblem.Conflict("Rejected after the effect was flushed");
        }

        var result = await executor.ExecuteAsync<EvaluationNoteMutationSuccess>(input, RejectAfterWriteAsync, token);
        var replay = await executor.ExecuteAsync<EvaluationNoteMutationSuccess>(input, RejectAfterWriteAsync, token);

        calls.ShouldBe(1);
        EvaluationMutationRejection.IsNotCommitted(result.Problem, input.OperationId).ShouldBeTrue();
        EvaluationMutationRejection.IsNotCommitted(replay.Problem, input.OperationId).ShouldBeTrue();
        await using var verify = fixture.CreateAdminContext();
        (await verify.Notes.CountAsync(note => note.PlayerCampaignAssignmentId == assignment, token)).ShouldBe(0);
        (await verify.EvaluationMutationReceipts.CountAsync(receipt => receipt.ClubId == club && receipt.OperationId == input.OperationId, token)).ShouldBe(1);
    }

    private async Task SetRejectionCampaignStatusAsync(long campaignId, long actor, CampaignStatus status, CancellationToken token)
    {
        await using var db = fixture.CreateAdminContext();
        var campaign = await db.Campaigns.SingleAsync(candidate => candidate.CampaignId == campaignId, token);
        campaign.Status = status;
        campaign.ClosedAt = status == CampaignStatus.Closed ? DateTimeOffset.UtcNow : null;
        campaign.ClosedById = status == CampaignStatus.Closed ? actor : null;
        await db.SaveChangesAsync(token);
    }
}

/// <summary>Pauses before any authorization lock, allowing a later request to settle the same operation first.</summary>
internal sealed class BeforeEvaluationAuthorizationGate : DbCommandInterceptor
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _gated;

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal) && Interlocked.Exchange(ref _gated, 1) == 0)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
        return result;
    }
}

/// <summary>Fails before the negative receipt commits, so no definitive outcome may be returned.</summary>
internal sealed class RejectionCommitFailure : DbTransactionInterceptor
{
    public int Calls { get; private set; }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    {
        Calls++;
        throw new InvalidOperationException("Injected failure before rejection receipt commit");
    }
}
