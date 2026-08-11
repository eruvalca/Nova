using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies evaluation note add, edit, and delete mutations remain correct when Npgsql retries a
/// failed transaction.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class EvaluationNoteRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies a transient failure raised before any commit does not let add verification convert a
    /// genuine not-found into a success.
    /// </summary>
    /// <remarks>
    /// The participation is missing before the request runs, so the correct answer is not-found. The
    /// injected failure interrupts the initial lookup, which means no commit was attempted and the
    /// absent participation belongs to no request rather than this one's ambiguous commit.
    /// </remarks>
    [Fact]
    public async Task AddNote_ReportsNotFound_WhenTransientFailurePrecedesCommitOnMissingParticipation()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, _, _) = await SeedNoteDataAsync(actorUserId, suffix, withNote: false);
        var missingAssignmentId = Random.Shared.NextInt64(1, long.MaxValue);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstNoteReadInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new EvaluationNoteService(
            factory,
            fixture.CurrentUser,
            NullLogger<EvaluationNoteService>.Instance);

        var result = await ((ICampaignEvaluationNoteService)service).AddAsync(
            new AddEvaluationNoteInput
            {
                PlayerCampaignAssignmentId = missingAssignmentId,
                Content = "New note content"
            },
            TestContext.Current.CancellationToken);

        failureInterceptor.FailureCount.ShouldBe(1);
        result.IsProblem.ShouldBeTrue("a missing participation must stay not-found after a pre-commit retry");
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies an add whose commit reached the database but surfaced a transient failure is
    /// reported as success and the note is persisted.
    /// </summary>
    [Fact]
    public async Task AddNote_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, assignmentId, _) = await SeedNoteDataAsync(actorUserId, suffix, withNote: false);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new EvaluationNoteService(
            factory,
            fixture.CurrentUser,
            NullLogger<EvaluationNoteService>.Instance);

        var content = $"Added note content {suffix}";
        var result = await ((ICampaignEvaluationNoteService)service).AddAsync(
            new AddEvaluationNoteInput { PlayerCampaignAssignmentId = assignmentId, Content = content },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NoteId.ShouldBeGreaterThan(0);
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Notes
            .SingleOrDefaultAsync(
                candidate => candidate.NoteId == result.Value.NoteId,
                TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.PlayerCampaignAssignmentId.ShouldBe(assignmentId);
        persisted.Content.ShouldBe(content);
    }

    /// <summary>
    /// Verifies a transient failure raised before any commit does not let edit verification convert a
    /// genuine not-found into a success or apply the edit.
    /// </summary>
    /// <remarks>
    /// The note is missing before the request runs, so the correct answer is not-found. The injected
    /// failure interrupts the initial lookup, which means no commit was attempted and the absent note
    /// belongs to no request rather than this one's ambiguous commit.
    /// </remarks>
    [Fact]
    public async Task EditNote_ReportsNotFound_WhenTransientFailurePrecedesCommitOnMissingNote()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, _, noteId) = await SeedNoteDataAsync(actorUserId, suffix);
        var missingNoteId = Random.Shared.NextInt64(1, long.MaxValue);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstNoteReadInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new EvaluationNoteService(
            factory,
            fixture.CurrentUser,
            NullLogger<EvaluationNoteService>.Instance);

        var result = await ((ICampaignEvaluationNoteService)service).EditAsync(
            new EditEvaluationNoteInput { NoteId = missingNoteId, Content = "Edited content" },
            TestContext.Current.CancellationToken);

        failureInterceptor.FailureCount.ShouldBe(1);
        result.IsProblem.ShouldBeTrue("a missing note must stay not-found after a pre-commit retry");
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Notes
            .SingleOrDefaultAsync(
                candidate => candidate.NoteId == noteId,
                TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull("the seeded note must be untouched by the failed edit");
        persisted.Content.ShouldBe($"Original note content {suffix}");
    }

    /// <summary>
    /// Verifies an edit whose commit reached the database but surfaced a transient failure is
    /// reported as success and the note content is updated.
    /// </summary>
    [Fact]
    public async Task EditNote_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, _, noteId) = await SeedNoteDataAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new EvaluationNoteService(
            factory,
            fixture.CurrentUser,
            NullLogger<EvaluationNoteService>.Instance);

        var editedContent = $"Edited note content {suffix}";
        var result = await ((ICampaignEvaluationNoteService)service).EditAsync(
            new EditEvaluationNoteInput { NoteId = noteId, Content = editedContent },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Notes
            .SingleOrDefaultAsync(
                candidate => candidate.NoteId == noteId,
                TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.Content.ShouldBe(editedContent);
    }

    /// <summary>
    /// Verifies a transient failure raised before any commit does not let delete verification convert
    /// a genuine not-found into a success or remove the note.
    /// </summary>
    /// <remarks>
    /// The note is missing before the request runs, so the correct answer is not-found. The injected
    /// failure interrupts the initial lookup, which means no commit was attempted and the absent note
    /// belongs to no request rather than this one's ambiguous commit.
    /// </remarks>
    [Fact]
    public async Task DeleteNote_ReportsNotFound_WhenTransientFailurePrecedesCommitOnMissingNote()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, _, noteId) = await SeedNoteDataAsync(actorUserId, suffix);
        var missingNoteId = Random.Shared.NextInt64(1, long.MaxValue);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstNoteReadInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new EvaluationNoteService(
            factory,
            fixture.CurrentUser,
            NullLogger<EvaluationNoteService>.Instance);

        var result = await ((ICampaignEvaluationNoteService)service).DeleteAsync(
            missingNoteId,
            TestContext.Current.CancellationToken);

        failureInterceptor.FailureCount.ShouldBe(1);
        result.IsProblem.ShouldBeTrue("a missing note must stay not-found after a pre-commit retry");
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Notes
            .SingleOrDefaultAsync(
                candidate => candidate.NoteId == noteId,
                TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull("the seeded note must be untouched by the failed delete");
    }

    /// <summary>
    /// Verifies a delete whose commit reached the database but surfaced a transient failure is
    /// reported as success and the note is removed.
    /// </summary>
    [Fact]
    public async Task DeleteNote_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, _, noteId) = await SeedNoteDataAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new EvaluationNoteService(
            factory,
            fixture.CurrentUser,
            NullLogger<EvaluationNoteService>.Instance);

        var result = await ((ICampaignEvaluationNoteService)service).DeleteAsync(
            noteId,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Notes
            .SingleOrDefaultAsync(
                candidate => candidate.NoteId == noteId,
                TestContext.Current.CancellationToken);
        persisted.ShouldBeNull();
    }

    /// <summary>
    /// Seeds one club, season, campaign, player, participation, and optional note owned by it.
    /// </summary>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="suffix">A unique suffix for generated names.</param>
    /// <param name="withNote">Whether the seeded evaluation note row should already exist.</param>
    /// <returns>The seeded club, campaign, participation, and note identifiers.</returns>
    private async Task<(long ClubId, long CampaignId, long AssignmentId, long NoteId)> SeedNoteDataAsync(
        long actorUserId,
        string suffix,
        bool withNote = true)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var seed = fixture.CreateAdminContext();
        var club = new ClubEntity
        {
            Name = $"Note Retry Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity
        {
            Name = $"Note Retry Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var campaign = new CampaignEntity
        {
            Name = $"Note Retry Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            Season = season,
            SeasonId = 0,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var player = new PlayerEntity
        {
            FirstName = "Note",
            LastName = $"Retry Player {suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };

        seed.AddRange(season, campaign, player);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = club.ClubId,
            CreatedById = actorUserId,
            PlacementOutcome = PlacementOutcome.Undecided,
            TryoutNumber = 7
        };
        seed.Add(assignment);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        long noteId = 0;
        if (withNote)
        {
            var note = new NoteEntity
            {
                Content = $"Original note content {suffix}",
                PlayerCampaignAssignmentId = assignment.PlayerCampaignAssignmentId,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            };
            seed.Add(note);
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
            noteId = note.NoteId;
        }

        return (club.ClubId, campaign.CampaignId, assignment.PlayerCampaignAssignmentId, noteId);
    }
}
