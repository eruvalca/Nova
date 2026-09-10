using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Features.Common;
using Nova.Integration.Tests.Data;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Cross-slice HTTP coverage for the duplicate tag-application race: when two approved club
/// members apply the same tag to the same assignment concurrently, both requests
/// succeed with one already-applied outcome, and exactly one durable row exists.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignTagApplicationRaceHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies that two concurrent tag applications for the same (assignment, tag) pair yield
    /// two Created responses, one already-applied receipt, and a single durable database row.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParallelTagApplicationForSameAssignmentAndTagYieldsOneOriginalOneAlreadyAppliedWithSingleDurableRowAsync(bool createInline)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (firstClient, secondClient, adminEmail, assignmentId) = await SeedTwoMemberClubWithTagAsync(
            "tag-race", cancellationToken);
        using var firstClientLease = firstClient;
        using var secondClientLease = secondClient;
        var tagId = await SeedingHelpers.InsertTagDefinitionAsync(fixture, assignmentId, adminEmail, "Winger", "#00CC00", cancellationToken);

        var operationA = Guid.CreateVersion7();
        var operationB = Guid.CreateVersion7();
        var label = $"Good control {Guid.NewGuid():N}";
        EvaluationOperationInput firstInput = createInline
            ? new CreateAndApplyCampaignTagInput { OperationId = operationA, PlayerCampaignAssignmentId = assignmentId, Label = $"  {label}  " }
            : new ApplyCampaignTagApplicationInput { OperationId = operationA, PlayerCampaignAssignmentId = assignmentId, PlayerTagId = tagId };
        EvaluationOperationInput secondInput = createInline
            ? new CreateAndApplyCampaignTagInput { OperationId = operationB, PlayerCampaignAssignmentId = assignmentId, Label = label.ToUpperInvariant() }
            : new ApplyCampaignTagApplicationInput { OperationId = operationB, PlayerCampaignAssignmentId = assignmentId, PlayerTagId = tagId };
        var route = createInline ? CampaignEndpoints.CreateAndApplyCampaignTag : CampaignEndpoints.ApplyCampaignTagApplication;
        var (responseA, responseB, clubId) = await SubmitContendedAsync(firstClient, secondClient, firstInput, secondInput, assignmentId, route, cancellationToken);
        using var responseALease = responseA;
        using var responseBLease = responseB;

        var statuses = new[] { responseA.StatusCode, responseB.StatusCode };
        statuses.ShouldAllBe(status => status == HttpStatusCode.Created);
        var receiptA = await responseA.Content.ReadFromJsonAsync<CampaignTagApplicationMutationSuccess>(cancellationToken);
        var receiptB = await responseB.Content.ReadFromJsonAsync<CampaignTagApplicationMutationSuccess>(cancellationToken);
        receiptA.CampaignTagApplicationId.ShouldBe(receiptB.CampaignTagApplicationId);
        receiptA.AlreadyApplied.ShouldNotBe(receiptB.AlreadyApplied);

        await using var context = fixture.CreateAdminContext();
        var durableRows = await context.CampaignTagApplications
            .Where(candidate => candidate.PlayerCampaignAssignmentId == assignmentId
                && candidate.PlayerTagId == receiptA.PlayerTagId)
            .ToListAsync(cancellationToken);
        durableRows.Count.ShouldBe(1);
        var originalReceipt = await context.EvaluationMutationReceipts.SingleAsync(receipt => receipt.ClubId == clubId && receipt.OperationId == (receiptA.AlreadyApplied ? operationB : operationA), cancellationToken);
        durableRows[0].CreatedById.ShouldBe(originalReceipt.ActorUserId);
        (await context.PlayerTags.CountAsync(tag => tag.ClubId == clubId && tag.PlayerTagId == receiptA.PlayerTagId, cancellationToken)).ShouldBe(1);
    }

    /// <summary>Proves both requests are waiting before release; transfers successful response ownership to the caller.</summary>
    private async Task<(HttpResponseMessage First, HttpResponseMessage Second, long ClubId)> SubmitContendedAsync(
        HttpClient firstClient, HttpClient secondClient, EvaluationOperationInput firstInput, EvaluationOperationInput secondInput,
        long assignmentId, string route, CancellationToken cancellationToken)
    {
        await using var gate = fixture.CreateAdminContext();
        var clubId = await gate.PlayerCampaignAssignments.Where(assignment => assignment.PlayerCampaignAssignmentId == assignmentId).Select(assignment => assignment.ClubId).SingleAsync(cancellationToken);
        await using var transaction = await gate.Database.BeginTransactionAsync(cancellationToken);
        await gate.AcquireClubMembershipLockAsync(clubId, cancellationToken);
        var applyA = firstClient.PostAsJsonAsync(route, (object)firstInput, cancellationToken);
        var applyB = secondClient.PostAsJsonAsync(route, (object)secondInput, cancellationToken);
        try
        {
            await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(gate, (long.MinValue / 32) + clubId, 2, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await Task.WhenAll(applyA, applyB);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            // Observe both requests while preserving the original contention failure.
            await ((Task)Task.WhenAll(applyA, applyB)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            // Both requests have finished; dispose any successful response before propagating the failure.
            if (applyA.IsCompletedSuccessfully)
            {
                (await applyA).Dispose();
            }
            if (applyB.IsCompletedSuccessfully)
            {
                (await applyB).Dispose();
            }
            throw;
        }

        return (await applyA, await applyB, clubId);
    }

    /// <summary>
    /// Seeds two approved members in one club and an active campaign with one participant.
    /// </summary>
    /// <param name="prefix">A stable e-mail prefix.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The two member clients, the admin e-mail, and the assignment identifier.</returns>
    private async Task<(HttpClient FirstClient, HttpClient SecondClient, string AdminEmail, long AssignmentId)>
        SeedTwoMemberClubWithTagAsync(string prefix, CancellationToken cancellationToken)
    {
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail($"{prefix}-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

#pragma warning disable CA2000 // Setup transfers clients to the caller on success and disposes both in the catch block on failure.
        var firstClient = fixture.CreateNovaHttpClient();
#pragma warning restore CA2000
        HttpClient? secondClient = null;
        try
        {
            var firstEmail = SeedingHelpers.UniqueEmail($"{prefix}-first");
            await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(firstClient, firstEmail, Password, cancellationToken);
            await SeedingHelpers.UpdateUserAsync(fixture, firstEmail, club.ClubId, cancellationToken);
            await SeedingHelpers.RefreshClubMembershipCookieAsync(firstClient, cancellationToken);

#pragma warning disable CA2000 // Setup transfers clients to the caller on success and disposes both in the catch block on failure.
            secondClient = fixture.CreateNovaHttpClient();
#pragma warning restore CA2000
            var secondEmail = SeedingHelpers.UniqueEmail($"{prefix}-second");
            await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(secondClient, secondEmail, Password, cancellationToken);
            await SeedingHelpers.UpdateUserAsync(fixture, secondEmail, club.ClubId, cancellationToken);
            await SeedingHelpers.RefreshClubMembershipCookieAsync(secondClient, cancellationToken);

            var seeded = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
                fixture, club.ClubId, adminEmail, prefix, participantCount: 1, placementOutcome: PlacementOutcome.Undecided, cancellationToken);
            return (firstClient, secondClient, adminEmail, seeded.AssignmentIds[0]);
        }
        catch
        {
            secondClient?.Dispose();
            firstClient.Dispose();
            throw;
        }
    }
}
