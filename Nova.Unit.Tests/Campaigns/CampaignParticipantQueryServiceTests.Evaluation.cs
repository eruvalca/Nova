using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.Unit.Tests.Account;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignParticipantQueryServiceTests
{
    [Fact]
    public async Task AdministratorHistoryDoesNotExposeEditingAnotherAuthorsNotesAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;
        using (var db = _harness.CreateAdminContext())
        {
            const long AuthorId = 1002;
            db.Users.Add(new NovaUserEntity { Id = AuthorId, ClubId = ClubAId, FirstName = "Original", LastName = "Author" });
            db.Roles.Add(new Microsoft.AspNetCore.Identity.IdentityRole<long> { Id = 10, Name = Nova.SharedKernel.Security.Roles.ClubAdmin, NormalizedName = Nova.SharedKernel.Security.Roles.ClubAdmin.ToUpperInvariant() });
            db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<long> { UserId = ClubAMemberId, RoleId = 10 });
            var seededNote = await db.Notes.SingleAsync(note => note.PlayerCampaignAssignmentId == _assignmentAId, TestContext.Current.CancellationToken);
            seededNote.CreatedById = AuthorId;
            seededNote.AuthorDisplayName = "Original Author";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var result = await EvaluationQuery().GetNotesAsync(HistoryInput(), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        var note = result.Value.Items.Single();
        note.AuthorDisplayName.ShouldBe("Original Author");
        note.CanEdit.ShouldBeFalse();
        note.CanDelete.ShouldBeFalse();
    }

    [Fact]
    public async Task ApplicationHistoryPagesTwentyAndCatalogIncludesAllActiveAppliedStatusAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        using (var db = _harness.CreateAdminContext())
        {
            for (var index = 0; index < 25; index++)
            {
                var tag = new PlayerTagEntity { CreationOperationId = Guid.NewGuid(), Name = $"Trait {index}", NormalizedName = $"TRAIT {index}", Color = "#006B6B", ClubId = ClubAId, CreatedById = ClubAMemberId };
                db.CampaignTagApplications.Add(new CampaignTagApplicationEntity { AuthorDisplayName = "Member A", CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = _assignmentAId, PlayerTag = tag, PlayerTagId = 0, ClubId = ClubAId, CreatedById = ClubAMemberId });
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var service = EvaluationQuery();
        var first = await service.GetApplicationsAsync(HistoryInput(), TestContext.Current.CancellationToken);
        first.Value.Items.Count.ShouldBe(20);
        first.Value.Next.ShouldNotBeNull();
        var second = await service.GetApplicationsAsync(HistoryInput() with { BeforeCreatedAt = first.Value.Next.CreatedAt, BeforeId = first.Value.Next.Id }, TestContext.Current.CancellationToken);
        second.Value.Items.Count.ShouldBe(6);
        second.Value.Next.ShouldBeNull();
        first.Value.Items.Concat(second.Value.Items).Select(item => item.CampaignTagApplicationId).Distinct().Count().ShouldBe(26);
        var choices = await service.GetTagChoicesAsync(new GetCampaignParticipantDetailInput { CampaignId = _campaignAId, PlayerCampaignAssignmentId = _assignmentAId }, TestContext.Current.CancellationToken);
        choices.IsSuccess.ShouldBeTrue();
        choices.Value.Count.ShouldBe(27);
        choices.Value.Count(choice => choice.ApplicationId.HasValue).ShouldBe(26);
    }

    private CampaignEvaluationQueryService EvaluationQuery() => new(
        new TestDbContextFactory<NovaReadDbContext>(() => _harness.CreateReadContext()), _harness.CurrentUser);

    private GetEvaluationHistoryInput HistoryInput() => new() { CampaignId = _campaignAId, PlayerCampaignAssignmentId = _assignmentAId };

    [Fact]
    public async Task EvaluationHistoryPagesTwentyNotesWithExclusiveTieBreakAndNoDuplicatesAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        using (var db = _harness.CreateAdminContext())
        {
            var sameTime = DateTimeOffset.UtcNow;
            for (var index = 0; index < 25; index++)
            {
                db.Notes.Add(new NoteEntity { AuthorDisplayName = "Seeded evaluator", CreationOperationId = Guid.NewGuid(), PlayerCampaignAssignmentId = _assignmentAId, ClubId = ClubAId, CreatedById = ClubAMemberId, CreatedAt = sameTime, Content = $"Observation {index}" });
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var service = EvaluationQuery();
        var first = await service.GetNotesAsync(HistoryInput(), TestContext.Current.CancellationToken);
        first.IsSuccess.ShouldBeTrue();
        first.Value.Items.Count.ShouldBe(20);
        first.Value.Next.ShouldNotBeNull();
        first.Value.Next.Id.ShouldBe(first.Value.Items[^1].NoteId);
        var second = await service.GetNotesAsync(HistoryInput() with { BeforeCreatedAt = first.Value.Next.CreatedAt, BeforeId = first.Value.Next.Id }, TestContext.Current.CancellationToken);
        second.IsSuccess.ShouldBeTrue();
        second.Value.Items.Count.ShouldBe(6);
        second.Value.Next.ShouldBeNull();
        first.Value.Items.Concat(second.Value.Items).Select(note => note.NoteId).Distinct().Count().ShouldBe(26);
        first.Value.Items.ShouldAllBe(note => note.CanEdit && note.CanDelete && note.Version != Guid.Empty);
        var applications = await service.GetApplicationsAsync(HistoryInput(), TestContext.Current.CancellationToken);
        applications.Value.Items.Count.ShouldBe(1);
        applications.Value.Next.ShouldBeNull();
    }

    [Theory]
    [InlineData("notes")]
    [InlineData("applications")]
    [InlineData("choices")]
    public async Task EvaluationRegionsRejectStaleMembershipIndependentlyAsync(string region)
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        using (var db = _harness.CreateAdminContext())
        {
            (await db.Users.SingleAsync(user => user.Id == ClubAMemberId, TestContext.Current.CancellationToken)).ClubId = null;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var service = EvaluationQuery();
        var kind = region switch
        {
            "notes" => (await service.GetNotesAsync(HistoryInput(), TestContext.Current.CancellationToken)).Problem.Kind,
            "applications" => (await service.GetApplicationsAsync(HistoryInput(), TestContext.Current.CancellationToken)).Problem.Kind,
            _ => (await service.GetTagChoicesAsync(new GetCampaignParticipantDetailInput { CampaignId = _campaignAId, PlayerCampaignAssignmentId = _assignmentAId }, TestContext.Current.CancellationToken)).Problem.Kind
        };
        kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task ClosedEvidenceRemainsReadableAndReopeningRestoresAuthorCapabilitiesAsync()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        using (var db = _harness.CreateAdminContext())
        {
            var campaign = (await db.Campaigns.SingleAsync(campaign => campaign.CampaignId == _campaignAId, TestContext.Current.CancellationToken));
            campaign.Status = CampaignStatus.Closed;
            campaign.ClosedAt = DateTimeOffset.UtcNow;
            campaign.ClosedById = ClubAMemberId;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var service = EvaluationQuery();
        var notes = await service.GetNotesAsync(HistoryInput(), TestContext.Current.CancellationToken);
        notes.Value.Items.Single().CanEdit.ShouldBeFalse();
        notes.Value.Items.Single().CanDelete.ShouldBeFalse();
        var applications = await service.GetApplicationsAsync(HistoryInput(), TestContext.Current.CancellationToken);
        applications.Value.Items.Single().CanRemove.ShouldBeFalse();
        using (var db = _harness.CreateAdminContext())
        {
            var campaign = (await db.Campaigns.SingleAsync(campaign => campaign.CampaignId == _campaignAId, TestContext.Current.CancellationToken));
            campaign.Status = CampaignStatus.Active;
            campaign.ClosedAt = null;
            campaign.ClosedById = null;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var reopened = await service.GetNotesAsync(HistoryInput(), TestContext.Current.CancellationToken);
        reopened.Value.Items.Single().CanEdit.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HistoryRejectsPartialCursorBeforeReadingEvidenceAsync(bool onlyTimestamp)
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        var input = HistoryInput() with { BeforeCreatedAt = onlyTimestamp ? DateTimeOffset.UtcNow : null, BeforeId = onlyTimestamp ? null : 1 };
        var notes = await EvaluationQuery().GetNotesAsync(input, TestContext.Current.CancellationToken);
        var applications = await EvaluationQuery().GetApplicationsAsync(input, TestContext.Current.CancellationToken);
        notes.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        applications.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }
}
