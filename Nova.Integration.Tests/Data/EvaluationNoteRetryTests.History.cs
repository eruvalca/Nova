using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class EvaluationNoteRetryTests
{
    private sealed class EvaluationReadFactory(NovaAppHostFixture app) : IDbContextFactory<NovaReadDbContext>
    {
        public NovaReadDbContext CreateDbContext() => app.CreateReadContext();
    }

    [Fact]
    public async Task PostgresEvidenceQueriesUseExclusiveTwentyItemKeysetPagesAsync()
    {
        var actor = BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue;
        var (clubId, campaignId, assignmentId, _) = await SeedNoteDataAsync(actor, Guid.NewGuid().ToString("N"));
        await AddHistoryAsync(actor, clubId, assignmentId);
        fixture.CurrentUser.UserId = actor;
        fixture.CurrentUser.ClubId = clubId;
        var service = new CampaignEvaluationQueryService(new EvaluationReadFactory(fixture), fixture.CurrentUser);
        var input = new GetEvaluationHistoryInput { CampaignId = campaignId, PlayerCampaignAssignmentId = assignmentId };
        var notes = await service.GetNotesAsync(input, TestContext.Current.CancellationToken);
        notes.IsSuccess.ShouldBeTrue();
        notes.Value.Items.Count.ShouldBe(20);
        notes.Value.Next.ShouldNotBeNull();
        var remainingNotes = await service.GetNotesAsync(input with { BeforeCreatedAt = notes.Value.Next.CreatedAt, BeforeId = notes.Value.Next.Id }, TestContext.Current.CancellationToken);
        remainingNotes.IsSuccess.ShouldBeTrue();
        remainingNotes.Value.Items.Count.ShouldBe(6);
        remainingNotes.Value.Next.ShouldBeNull();
        notes.Value.Items.Concat(remainingNotes.Value.Items).Select(note => note.NoteId).Distinct().Count().ShouldBe(26);
        var applications = await service.GetApplicationsAsync(input, TestContext.Current.CancellationToken);
        applications.IsSuccess.ShouldBeTrue();
        applications.Value.Items.Count.ShouldBe(20);
        applications.Value.Next.ShouldNotBeNull();
        var remainingApplications = await service.GetApplicationsAsync(input with { BeforeCreatedAt = applications.Value.Next.CreatedAt, BeforeId = applications.Value.Next.Id }, TestContext.Current.CancellationToken);
        remainingApplications.IsSuccess.ShouldBeTrue();
        remainingApplications.Value.Items.Count.ShouldBe(5);
        remainingApplications.Value.Next.ShouldBeNull();
        applications.Value.Items.Concat(remainingApplications.Value.Items).Select(item => item.CampaignTagApplicationId).Distinct().Count().ShouldBe(25);
    }

    [Fact]
    public async Task NoteVersionMappingRejectsAStaleProviderUpdateAsync()
    {
        var actor = BitConverter.ToInt64(Guid.NewGuid().ToByteArray()) & long.MaxValue;
        var (clubId, _, _, noteId) = await SeedNoteDataAsync(actor, Guid.NewGuid().ToString("N"));
        fixture.CurrentUser.UserId = actor;
        fixture.CurrentUser.ClubId = clubId;
        await using var first = fixture.CreateTenantContext();
        await using var second = fixture.CreateTenantContext();
        var firstNote = await first.Notes.SingleAsync(note => note.NoteId == noteId, TestContext.Current.CancellationToken);
        var secondNote = await second.Notes.SingleAsync(note => note.NoteId == noteId, TestContext.Current.CancellationToken);
        firstNote.Version = Guid.NewGuid();
        firstNote.Content = "Newer content";
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);
        secondNote.Content = "Stale content";
        secondNote.Version = Guid.NewGuid();
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(TestContext.Current.CancellationToken));
        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Notes.SingleAsync(note => note.NoteId == noteId, TestContext.Current.CancellationToken);
        persisted.Content.ShouldBe("Newer content");
        persisted.Version.ShouldBe(firstNote.Version);
    }

    private async Task AddHistoryAsync(long actor, long clubId, long assignmentId)
    {
        await using var db = fixture.CreateAdminContext();
        for (var index = 0; index < 25; index++)
        {
            db.Notes.Add(new NoteEntity { CreationOperationId = Guid.NewGuid(), Content = $"Observation {index}", PlayerCampaignAssignmentId = assignmentId, ClubId = clubId, CreatedById = actor });
            var tag = new PlayerTagEntity { CreationOperationId = Guid.NewGuid(), Name = $"Trait {index}", NormalizedName = $"TRAIT {index}", Color = "#006B6B", ClubId = clubId, CreatedById = actor };
            db.CampaignTagApplications.Add(new CampaignTagApplicationEntity { CreationOperationId = Guid.NewGuid(), PlayerTag = tag, PlayerTagId = 0, PlayerCampaignAssignmentId = assignmentId, ClubId = clubId, CreatedById = actor });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
