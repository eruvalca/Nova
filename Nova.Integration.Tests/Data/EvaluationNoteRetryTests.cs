using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using OneOf.Types;
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
    /// Verifies PostgreSQL rejects two notes in the same club with the same creation-operation
    /// identifier.
    /// </summary>
    [Fact]
    public async Task CreationOperationId_RejectsDuplicateWithinClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var creationOperationId = Guid.CreateVersion7();
        var (clubId, _, assignmentId, _) = await SeedNoteDataAsync(actorUserId, suffix, withNote: false);

        await using var db = fixture.CreateAdminContext();
        db.Notes.AddRange(
            CreateNote("First note", assignmentId, clubId, actorUserId, creationOperationId),
            CreateNote("Second note", assignmentId, clubId, actorUserId, creationOperationId));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

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
    /// Verifies a failed commit is retried instead of being mistaken for an older identical note.
    /// </summary>
    [Fact]
    public async Task AddNote_RetriesFailedCommit_WhenIdenticalNoteAlreadyExists()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, assignmentId, existingNoteId) = await SeedNoteDataAsync(actorUserId, suffix);
        var content = $"Original note content {suffix}";

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstTransactionCommitInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new EvaluationNoteService(
            factory,
            fixture.CurrentUser,
            NullLogger<EvaluationNoteService>.Instance);

        var result = await ((ICampaignEvaluationNoteService)service).AddAsync(
            new AddEvaluationNoteInput { PlayerCampaignAssignmentId = assignmentId, Content = content },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NoteId.ShouldNotBe(existingNoteId);
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var persistedNoteIds = await verify.Notes
            .Where(candidate => candidate.PlayerCampaignAssignmentId == assignmentId
                && candidate.Content == content)
            .OrderBy(candidate => candidate.NoteId)
            .Select(candidate => candidate.NoteId)
            .ToListAsync(TestContext.Current.CancellationToken);
        persistedNoteIds.ShouldBe([existingNoteId, result.Value.NoteId]);
    }

    /// <summary>
    /// Verifies an add whose commit reached the database but surfaced a transient failure does not
    /// replay the mutation when a concurrent delete removed the note before verification ran.
    /// </summary>
    /// <remarks>
    /// The first add commits ambiguously and pauses just before its receipt-verification read. A
    /// competing delete then removes the created note and commits. Verification consults the durable
    /// operation receipt (which survives the delete) rather than the now-deleted note row, so the
    /// paused add reports success and never re-inserts the note.
    /// </remarks>
    [Fact]
    public async Task AddNote_AmbiguousCommitThenConcurrentDelete_DoesNotResurrectNote()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, assignmentId, _) = await SeedNoteDataAsync(actorUserId, suffix, withNote: false);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var gateInterceptor = new GateReceiptVerificationInterceptor();
        var addFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor,
            gateInterceptor);
        var deleteFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor());
        var addService = new EvaluationNoteService(
            addFactory,
            fixture.CurrentUser,
            NullLogger<EvaluationNoteService>.Instance);
        var deleteService = new EvaluationNoteService(
            deleteFactory,
            fixture.CurrentUser,
            NullLogger<EvaluationNoteService>.Instance);

        Task<ServiceResult<EvaluationNoteMutationSuccess>> addTask;
        try
        {
            // The add pauses after its ambiguous commit, just before verification reads the receipt.
            addTask = ((ICampaignEvaluationNoteService)addService).AddAsync(
                new AddEvaluationNoteInput
                {
                    PlayerCampaignAssignmentId = assignmentId,
                    Content = $"Added note content {suffix}"
                },
                TestContext.Current.CancellationToken);
            await gateInterceptor.WaitForVerificationAttemptAsync(TestContext.Current.CancellationToken);

            // The commit is durable, so the note is visible; a competing delete removes it.
            long noteId;
            await using (var locate = fixture.CreateAdminContext())
            {
                noteId = await locate.Notes
                    .Where(candidate => candidate.PlayerCampaignAssignmentId == assignmentId)
                    .Select(candidate => candidate.NoteId)
                    .SingleAsync(TestContext.Current.CancellationToken);
            }

            var deleteResult = await ((ICampaignEvaluationNoteService)deleteService).DeleteAsync(
                noteId,
                TestContext.Current.CancellationToken);
            deleteResult.IsSuccess.ShouldBeTrue("the delete must commit while the add is paused at verification");

            gateInterceptor.Release();
            var addResult = await addTask;
            addResult.IsSuccess.ShouldBeTrue(
                "the paused add must verify against its durable receipt and report success");
        }
        finally
        {
            gateInterceptor.Release();
        }

        failureInterceptor.FailureCount.ShouldBe(1);
        await using var verify = fixture.CreateAdminContext();
        var resurrected = await verify.Notes
            .AnyAsync(
                candidate => candidate.PlayerCampaignAssignmentId == assignmentId,
                TestContext.Current.CancellationToken);
        resurrected.ShouldBeFalse("the delete must remain authoritative and the add must not replay");
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
    /// Verifies an edit whose commit reached the database but surfaced a transient failure does not
    /// replay the mutation when a newer edit committed before verification ran.
    /// </summary>
    /// <remarks>
    /// The first edit commits ambiguously and pauses just before its receipt-verification read. A
    /// newer edit then commits different content. Verification consults the first edit's durable
    /// operation receipt rather than comparing note content, so the paused edit reports success and
    /// never overwrites the newer content.
    /// </remarks>
    [Fact]
    public async Task EditNote_AmbiguousCommitThenNewerEdit_DoesNotOverwriteNewerContent()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, _, _, noteId) = await SeedNoteDataAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var gateInterceptor = new GateReceiptVerificationInterceptor();
        var firstFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor,
            gateInterceptor);
        var secondFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            new NoOpInterceptor());
        var firstService = new EvaluationNoteService(
            firstFactory,
            fixture.CurrentUser,
            NullLogger<EvaluationNoteService>.Instance);
        var secondService = new EvaluationNoteService(
            secondFactory,
            fixture.CurrentUser,
            NullLogger<EvaluationNoteService>.Instance);

        Task<ServiceResult<Success>> firstEdit;
        try
        {
            // The first edit pauses after its ambiguous commit, just before verification reads the receipt.
            firstEdit = ((ICampaignEvaluationNoteService)firstService).EditAsync(
                new EditEvaluationNoteInput { NoteId = noteId, Content = "First edit content" },
                TestContext.Current.CancellationToken);
            await gateInterceptor.WaitForVerificationAttemptAsync(TestContext.Current.CancellationToken);

            // A newer edit commits different content while the first edit is paused.
            var newerResult = await ((ICampaignEvaluationNoteService)secondService).EditAsync(
                new EditEvaluationNoteInput { NoteId = noteId, Content = "Newer edit content" },
                TestContext.Current.CancellationToken);
            newerResult.IsSuccess.ShouldBeTrue("the newer edit must commit while the first is paused at verification");

            gateInterceptor.Release();
            var firstResult = await firstEdit;
            firstResult.IsSuccess.ShouldBeTrue(
                "the paused edit must verify against its durable receipt and report success");
        }
        finally
        {
            gateInterceptor.Release();
        }

        failureInterceptor.FailureCount.ShouldBe(1);
        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Notes
            .SingleOrDefaultAsync(
                candidate => candidate.NoteId == noteId,
                TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.Content.ShouldBe(
            "Newer edit content",
            "the newer edit must survive and the paused edit must not replay");
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
            CreationOperationId = Guid.NewGuid(),
            Name = $"Note Retry Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Note Retry Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
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
            CreationOperationId = Guid.NewGuid(),
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
                CreationOperationId = Guid.NewGuid(),
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

    /// <summary>
    /// Creates a note entity for direct database constraint verification.
    /// </summary>
    private static NoteEntity CreateNote(
        string content,
        long assignmentId,
        long clubId,
        long actorUserId,
        Guid creationOperationId)
        => new()
        {
            Content = content,
            CreationOperationId = creationOperationId,
            PlayerCampaignAssignmentId = assignmentId,
            ClubId = clubId,
            CreatedById = actorUserId
        };
}
