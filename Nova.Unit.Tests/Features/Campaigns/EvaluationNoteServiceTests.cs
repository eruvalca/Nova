using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Features.Campaigns;

/// <summary>
/// Unit tests for <see cref="EvaluationNoteService"/> covering add, edit, and delete authorization,
/// campaign-status guards, cross-tenant rejection, and input validation.
/// </summary>
public sealed partial class EvaluationNoteServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAMember1Id = 110;
    private const long ClubAMember2Id = 111;
    private const long ClubBMemberId = 210;
    private const long ClubAAdminId = 112;

    private readonly TenancyTestHarness _harness = new();
    private long _assignmentId;
    private long _closedAssignmentId;
    private long _existingNoteId;

    /// <summary>
    /// Seeds reference data and returns the test fixture to a known state.
    /// </summary>
    public EvaluationNoteServiceTests()
    {
        Seed();
    }

    /// <inheritdoc/>
    public void Dispose() => _harness.Dispose();

    // ── Add ────────────────────────────────────────────────────────────────────

    /// <summary>Verifies that a club member can add a note to an Active campaign participation.</summary>
    [Fact]
    public async Task AddSucceedsForClubMemberAsync()
    {
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            PlayerCampaignAssignmentId = _assignmentId,
            Content = "Good footwork."
        }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NoteId.ShouldBeGreaterThan(0);

        using var db = _harness.CreateAdminContext();
        var addedNote = await db.Notes
            .Where(note => note.PlayerCampaignAssignmentId == _assignmentId && note.Content == "Good footwork.")
            .OrderByDescending(note => note.NoteId)
            .FirstAsync(TestContext.Current.CancellationToken);
        addedNote.ClubId.ShouldBe(ClubAId);
        addedNote.CreatedById.ShouldBe(ClubAMember1Id);
    }

    /// <summary>Verifies that an unauthenticated caller cannot add a note.</summary>
    [Fact]
    public async Task AddReturnsForbiddenForAnonymousUserAsync()
    {
        ActAs(userId: null, clubId: null);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            PlayerCampaignAssignmentId = _assignmentId,
            Content = "Should fail."
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies that a user with no club cannot add a note.</summary>
    [Fact]
    public async Task AddReturnsForbiddenForUserWithoutClubAsync()
    {
        ActAs(userId: 999, clubId: null);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            PlayerCampaignAssignmentId = _assignmentId,
            Content = "Should fail."
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies that adding to a participation from another club returns NotFound.</summary>
    [Fact]
    public async Task AddReturnsNotFoundForCrossTenantAssignmentAsync()
    {
        ActAs(ClubBMemberId, ClubBId);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            PlayerCampaignAssignmentId = _assignmentId, // belongs to Club A
            Content = "Cross-tenant attempt."
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies that adding to a Closed campaign participation returns a conflict.</summary>
    [Fact]
    public async Task AddReturnsConflictForClosedCampaignAsync()
    {
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            PlayerCampaignAssignmentId = _closedAssignmentId,
            Content = "Should fail — campaign is closed."
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("This campaign is read-only. Refresh to see its current status; keep or copy your draft.");
    }

    /// <summary>
    /// Verifies a Draft campaign rejects note creation without adding a note, mutation receipt, or activity event.
    /// </summary>
    [Fact]
    public async Task AddReturnsConflictWithoutWritesOrActivityForDraftCampaignAsync()
    {
        await MakeCampaignDraftAsync(_assignmentId);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            PlayerCampaignAssignmentId = _assignmentId,
            Content = "Draft campaign note."
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("This campaign is read-only. Refresh to see its current status; keep or copy your draft.");

        await using var verify = _harness.CreateAdminContext();
        (await verify.Notes.AnyAsync(
            note => note.Content == "Draft campaign note.",
            TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.EvaluationMutationReceipts.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.ActivityEvents.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    /// <summary>Verifies that blank content fails validation.</summary>
    [Fact]
    public async Task AddReturnsValidationErrorForBlankContentAsync()
    {
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            PlayerCampaignAssignmentId = _assignmentId,
            Content = "   "
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    // ── Edit ───────────────────────────────────────────────────────────────────

    /// <summary>Verifies that the original author can edit their own note.</summary>
    [Fact]
    public async Task EditSucceedsForAuthorAsync()
    {
        // Note was created by ClubAMember1Id (see Seed)
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();
        using var originalDb = _harness.CreateAdminContext();
        var originalNote = (await originalDb.Notes.SingleAsync(note => note.NoteId == _existingNoteId, TestContext.Current.CancellationToken));
        var originalCreatedById = originalNote.CreatedById;

        var result = await sut.EditAsync(new EditEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            NoteId = _existingNoteId,
            ExpectedVersion = await NoteVersionAsync(_existingNoteId),
            Content = "Updated content."
        }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        using var db = _harness.CreateAdminContext();
        var editedNote = (await db.Notes.SingleAsync(note => note.NoteId == _existingNoteId, TestContext.Current.CancellationToken));
        editedNote.Content.ShouldBe("Updated content.");
        editedNote.CreatedById.ShouldBe(originalCreatedById);
        editedNote.ModifiedAt.ShouldNotBeNull();
        editedNote.ModifiedById.ShouldBe(ClubAMember1Id);
    }

    /// <summary>Verifies that administrator privileges do not permit editing another author�s note.</summary>
    [Fact]
    public async Task EditReturnsForbiddenForNonAuthorClubAdminAsync()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var sut = CreateService();

        var result = await sut.EditAsync(new EditEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            NoteId = _existingNoteId,
            ExpectedVersion = await NoteVersionAsync(_existingNoteId),
            Content = "Admin override."
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Notes.SingleAsync(note => note.NoteId == _existingNoteId, TestContext.Current.CancellationToken)).Content.ShouldBe("Initial note.");
        (await verify.EvaluationMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    /// <summary>Verifies that a non-author, non-admin club member cannot edit the note.</summary>
    [Fact]
    public async Task EditReturnsForbiddenForNonAuthorNonAdminAsync()
    {
        ActAs(ClubAMember2Id, ClubAId, isClubAdmin: false);
        var sut = CreateService();

        var result = await sut.EditAsync(new EditEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            NoteId = _existingNoteId,
            ExpectedVersion = await NoteVersionAsync(_existingNoteId),
            Content = "Unauthorized edit."
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies that editing a note whose campaign is closed returns a conflict.</summary>
    [Fact]
    public async Task EditReturnsConflictForClosedCampaignAsync()
    {
        var closedNoteId = SeedNote(_closedAssignmentId, ClubAId, ClubAMember1Id);

        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.EditAsync(new EditEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            NoteId = closedNoteId,
            ExpectedVersion = await NoteVersionAsync(closedNoteId),
            Content = "Should fail — campaign is closed."
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("This campaign is read-only. Refresh to see its current status; keep or copy your draft.");
    }

    /// <summary>
    /// Verifies a Draft campaign rejects note edits without changing the note or recording side effects.
    /// </summary>
    [Fact]
    public async Task EditReturnsConflictWithoutWritesOrActivityForDraftCampaignAsync()
    {
        await MakeCampaignDraftAsync(_assignmentId);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var sut = CreateService();

        var result = await sut.EditAsync(new EditEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            NoteId = _existingNoteId,
            ExpectedVersion = await NoteVersionAsync(_existingNoteId),
            Content = "Draft campaign edit."
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("This campaign is read-only. Refresh to see its current status; keep or copy your draft.");

        await using var verify = _harness.CreateAdminContext();
        var note = await verify.Notes.SingleAsync(
            candidate => candidate.NoteId == _existingNoteId,
            TestContext.Current.CancellationToken);
        note.Content.ShouldBe("Initial note.");
        note.ModifiedAt.ShouldBeNull();
        note.ModifiedById.ShouldBeNull();
        (await verify.EvaluationMutationReceipts.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.ActivityEvents.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    /// <summary>Verifies that a cross-tenant edit attempt returns NotFound, not an error exposing the note.</summary>
    [Fact]
    public async Task EditReturnsNotFoundForCrossTenantNoteAsync()
    {
        ActAs(ClubBMemberId, ClubBId);
        var sut = CreateService();

        var result = await sut.EditAsync(new EditEvaluationNoteInput
        {
            OperationId = Guid.CreateVersion7(),
            NoteId = _existingNoteId,
            ExpectedVersion = await NoteVersionAsync(_existingNoteId), // belongs to Club A
            Content = "Cross-tenant edit."
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    /// <summary>Verifies that the original author can delete their own note.</summary>
    [Fact]
    public async Task DeleteSucceedsForAuthorAsync()
    {
        var noteId = SeedNote(_assignmentId, ClubAId, ClubAMember1Id);
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.DeleteAsync(new DeleteEvaluationNoteInput { NoteId = noteId, ExpectedVersion = await NoteVersionAsync(noteId), OperationId = Guid.CreateVersion7() }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>Verifies that administrator privileges do not permit deleting another author�s note.</summary>
    [Fact]
    public async Task DeleteReturnsForbiddenForNonAuthorClubAdminAsync()
    {
        var noteId = SeedNote(_assignmentId, ClubAId, ClubAMember1Id);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var sut = CreateService();

        var result = await sut.DeleteAsync(new DeleteEvaluationNoteInput { NoteId = noteId, ExpectedVersion = await NoteVersionAsync(noteId), OperationId = Guid.CreateVersion7() }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        await using var verify = _harness.CreateAdminContext();
        (await verify.Notes.AnyAsync(note => note.NoteId == noteId, TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await verify.EvaluationMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    /// <summary>Verifies that a non-author, non-admin club member cannot delete the note.</summary>
    [Fact]
    public async Task DeleteReturnsForbiddenForNonAuthorNonAdminAsync()
    {
        var noteId = SeedNote(_assignmentId, ClubAId, ClubAMember1Id);
        ActAs(ClubAMember2Id, ClubAId, isClubAdmin: false);
        var sut = CreateService();

        var result = await sut.DeleteAsync(new DeleteEvaluationNoteInput { NoteId = noteId, ExpectedVersion = await NoteVersionAsync(noteId), OperationId = Guid.CreateVersion7() }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies that deleting a note on a closed campaign returns a conflict.</summary>
    [Fact]
    public async Task DeleteReturnsConflictForClosedCampaignAsync()
    {
        var noteId = SeedNote(_closedAssignmentId, ClubAId, ClubAMember1Id);
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.DeleteAsync(new DeleteEvaluationNoteInput { NoteId = noteId, ExpectedVersion = await NoteVersionAsync(noteId), OperationId = Guid.CreateVersion7() }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("This campaign is read-only. Refresh to see its current status; keep or copy your draft.");
    }

    /// <summary>
    /// Verifies a Draft campaign rejects note deletion without deleting the note or recording side effects.
    /// </summary>
    [Fact]
    public async Task DeleteReturnsConflictWithoutWritesOrActivityForDraftCampaignAsync()
    {
        await MakeCampaignDraftAsync(_assignmentId);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var sut = CreateService();

        var result = await sut.DeleteAsync(new DeleteEvaluationNoteInput { NoteId = _existingNoteId, ExpectedVersion = await NoteVersionAsync(_existingNoteId), OperationId = Guid.CreateVersion7() }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe("This campaign is read-only. Refresh to see its current status; keep or copy your draft.");

        await using var verify = _harness.CreateAdminContext();
        (await verify.Notes.AnyAsync(
            note => note.NoteId == _existingNoteId,
            TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await verify.EvaluationMutationReceipts.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.ActivityEvents.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    /// <summary>Verifies that a cross-tenant delete attempt returns NotFound.</summary>
    [Fact]
    public async Task DeleteReturnsNotFoundForCrossTenantNoteAsync()
    {
        ActAs(ClubBMemberId, ClubBId);
        var sut = CreateService();

        var result = await sut.DeleteAsync(new DeleteEvaluationNoteInput { NoteId = _existingNoteId, ExpectedVersion = await NoteVersionAsync(_existingNoteId), OperationId = Guid.CreateVersion7() }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Sets the simulated current user for subsequent service calls.</summary>
    private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>Creates the service under test using the shared harness.</summary>
    private EvaluationNoteService CreateService() =>
        new(new Nova.Unit.Tests.Account.TestDbContextFactory<NovaDbContext>(() => _harness.CreateTenantContext()), _harness.CurrentUser, NullLogger<EvaluationNoteService>.Instance);

    /// <summary>
    /// Changes the campaign for an existing assignment from Active to Draft for mutation rejection tests.
    /// </summary>
    /// <param name="assignmentId">The assignment whose campaign should become Draft.</param>
    private async Task MakeCampaignDraftAsync(long assignmentId)
    {
#pragma warning disable MA0004 // Dispose within the original test scope and retain the test runner synchronization context.
        await using var db = _harness.CreateAdminContext();
#pragma warning restore MA0004
        var campaign = await db.PlayerCampaignAssignments
            .Where(assignment => assignment.PlayerCampaignAssignmentId == assignmentId)
            .Select(assignment => assignment.Campaign)
            .SingleAsync(TestContext.Current.CancellationToken);
        campaign.Status = CampaignStatus.Draft;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Seeds clubs, users, players, campaigns, and participations needed for the service tests.
    /// </summary>
#pragma warning disable MA0051 // Keep the complete arrangement, operation, and assertions together as one regression scenario.
    private void Seed()
#pragma warning restore MA0051
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAMember1Id },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });

        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAMember1Id, FirstName = "Alice", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMember2Id, FirstName = "Aaron", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bob", LastName = "B", ClubId = ClubBId });

        db.Roles.Add(new Microsoft.AspNetCore.Identity.IdentityRole<long> { Id = 10, Name = Nova.SharedKernel.Security.Roles.ClubAdmin, NormalizedName = Nova.SharedKernel.Security.Roles.ClubAdmin.ToUpperInvariant() });
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<long> { UserId = ClubAAdminId, RoleId = 10 });

        var player = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "Player",
            DateOfBirth = new DateOnly(2010, 1, 1),
            GraduationYear = 2028,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        db.Players.Add(player);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Season 2026",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        db.Seasons.Add(season);
        db.SaveChanges();

        var activeCampaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Active Campaign",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        var closedCampaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Closed Campaign",
            StartDate = new DateOnly(2026, 5, 1),
            Status = CampaignStatus.Closed,
            ClosedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ClosedById = ClubAAdminId,
            SeasonId = season.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        db.Campaigns.AddRange(activeCampaign, closedCampaign);
        db.SaveChanges();

        var activeAssignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = activeCampaign.CampaignId,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        var closedAssignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = closedCampaign.CampaignId,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        db.PlayerCampaignAssignments.AddRange(activeAssignment, closedAssignment);
        db.SaveChanges();

        _assignmentId = activeAssignment.PlayerCampaignAssignmentId;
        _closedAssignmentId = closedAssignment.PlayerCampaignAssignmentId;

        // Seed one note authored by ClubAMember1Id for edit/delete tests.
        var existingNote = new NoteEntity
        {
            AuthorDisplayName = "Alice A",
            CreationOperationId = Guid.NewGuid(),
            Content = "Initial note.",
            PlayerCampaignAssignmentId = _assignmentId,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        db.Notes.Add(existingNote);
        db.SaveChanges();

        _existingNoteId = existingNote.NoteId;
    }

    /// <summary>
    /// Seeds an additional note on the given assignment and returns its generated id.
    /// </summary>
    /// <param name="assignmentId">The assignment to attach the note to.</param>
    /// <param name="clubId">The club the note belongs to.</param>
    /// <param name="authorId">The note author identifier.</param>
    /// <returns>The generated note identifier.</returns>
    private long SeedNote(long assignmentId, long clubId, long authorId)
    {
        using var db = _harness.CreateAdminContext();
        var note = new NoteEntity
        {
            AuthorDisplayName = "Alice A",
            CreationOperationId = Guid.NewGuid(),
            Content = "Seeded note.",
            PlayerCampaignAssignmentId = assignmentId,
            ClubId = clubId,
            CreatedById = authorId
        };
        db.Notes.Add(note);
        db.SaveChanges();
        return note.NoteId;
    }
}
