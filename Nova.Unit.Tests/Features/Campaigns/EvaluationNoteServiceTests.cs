using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Campaigns;
using Nova.Shared.Enums;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Features.Campaigns;

/// <summary>
/// A minimal <see cref="IDbContextFactory{TContext}"/> that creates contexts from the shared
/// in-memory SQLite connection in <see cref="TenancyTestHarness"/>.
/// </summary>
file sealed class HarnessDbContextFactory(TenancyTestHarness harness) : IDbContextFactory<NovaDbContext>
{
    /// <inheritdoc />
    public NovaDbContext CreateDbContext() => harness.CreateTenantContext();

    /// <inheritdoc />
    public Task<NovaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(harness.CreateTenantContext());
}

/// <summary>
/// Unit tests for <see cref="EvaluationNoteService"/> covering add, edit, and delete authorization,
/// campaign-status guards, cross-tenant rejection, and input validation.
/// </summary>
public sealed class EvaluationNoteServiceTests : IDisposable
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
    public async Task Add_Succeeds_ForClubMember()
    {
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            PlayerCampaignAssignmentId = _assignmentId,
            Content = "Good footwork."
        }, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue(); // Success

        using var db = _harness.CreateAdminContext();
        var addedNote = db.Notes
            .Where(note => note.PlayerCampaignAssignmentId == _assignmentId && note.Content == "Good footwork.")
            .OrderByDescending(note => note.NoteId)
            .First();
        addedNote.ClubId.ShouldBe(ClubAId);
        addedNote.CreatedById.ShouldBe(ClubAMember1Id);
    }

    /// <summary>Verifies that an unauthenticated caller cannot add a note.</summary>
    [Fact]
    public async Task Add_ReturnsForbidden_ForAnonymousUser()
    {
        ActAs(userId: null, clubId: null);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            PlayerCampaignAssignmentId = _assignmentId,
            Content = "Should fail."
        }, TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue(); // LifecycleForbidden
    }

    /// <summary>Verifies that a user with no club cannot add a note.</summary>
    [Fact]
    public async Task Add_ReturnsForbidden_ForUserWithoutClub()
    {
        ActAs(userId: 999, clubId: null);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            PlayerCampaignAssignmentId = _assignmentId,
            Content = "Should fail."
        }, TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue(); // LifecycleForbidden
    }

    /// <summary>Verifies that adding to a participation from another club returns NotFound.</summary>
    [Fact]
    public async Task Add_ReturnsNotFound_ForCrossTenantAssignment()
    {
        ActAs(ClubBMemberId, ClubBId);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            PlayerCampaignAssignmentId = _assignmentId, // belongs to Club A
            Content = "Cross-tenant attempt."
        }, TestContext.Current.CancellationToken);

        result.IsT2.ShouldBeTrue(); // NotFound
    }

    /// <summary>Verifies that adding to a Closed campaign participation returns a conflict.</summary>
    [Fact]
    public async Task Add_ReturnsConflict_ForClosedCampaign()
    {
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            PlayerCampaignAssignmentId = _closedAssignmentId,
            Content = "Should fail — campaign is closed."
        }, TestContext.Current.CancellationToken);

        result.IsT4.ShouldBeTrue(); // LifecycleConflict
    }

    /// <summary>Verifies that blank content fails validation.</summary>
    [Fact]
    public async Task Add_ReturnsValidationError_ForBlankContent()
    {
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.AddAsync(new AddEvaluationNoteInput
        {
            PlayerCampaignAssignmentId = _assignmentId,
            Content = "   "
        }, TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue(); // Error<...>
    }

    // ── Edit ───────────────────────────────────────────────────────────────────

    /// <summary>Verifies that the original author can edit their own note.</summary>
    [Fact]
    public async Task Edit_Succeeds_ForAuthor()
    {
        // Note was created by ClubAMember1Id (see Seed)
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();
        using var originalDb = _harness.CreateAdminContext();
        var originalNote = originalDb.Notes.Single(note => note.NoteId == _existingNoteId);
        var originalCreatedById = originalNote.CreatedById;

        var result = await sut.EditAsync(new EditEvaluationNoteInput
        {
            NoteId = _existingNoteId,
            Content = "Updated content."
        }, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue(); // Success

        using var db = _harness.CreateAdminContext();
        var editedNote = db.Notes.Single(note => note.NoteId == _existingNoteId);
        editedNote.Content.ShouldBe("Updated content.");
        editedNote.CreatedById.ShouldBe(originalCreatedById);
        editedNote.ModifiedAt.ShouldNotBeNull();
        editedNote.ModifiedById.ShouldBe(ClubAMember1Id);
    }

    /// <summary>Verifies that a club administrator can edit any note in their club.</summary>
    [Fact]
    public async Task Edit_Succeeds_ForClubAdmin()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var sut = CreateService();

        var result = await sut.EditAsync(new EditEvaluationNoteInput
        {
            NoteId = _existingNoteId,
            Content = "Admin override."
        }, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue(); // Success
    }

    /// <summary>Verifies that a non-author, non-admin club member cannot edit the note.</summary>
    [Fact]
    public async Task Edit_ReturnsForbidden_ForNonAuthorNonAdmin()
    {
        ActAs(ClubAMember2Id, ClubAId, isClubAdmin: false);
        var sut = CreateService();

        var result = await sut.EditAsync(new EditEvaluationNoteInput
        {
            NoteId = _existingNoteId,
            Content = "Unauthorized edit."
        }, TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue(); // LifecycleForbidden
    }

    /// <summary>Verifies that editing a note whose campaign is closed returns a conflict.</summary>
    [Fact]
    public async Task Edit_ReturnsConflict_ForClosedCampaign()
    {
        long closedNoteId = SeedNote(_closedAssignmentId, ClubAId, ClubAMember1Id);

        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.EditAsync(new EditEvaluationNoteInput
        {
            NoteId = closedNoteId,
            Content = "Should fail — campaign is closed."
        }, TestContext.Current.CancellationToken);

        result.IsT4.ShouldBeTrue(); // LifecycleConflict
    }

    /// <summary>Verifies that a cross-tenant edit attempt returns NotFound, not an error exposing the note.</summary>
    [Fact]
    public async Task Edit_ReturnsNotFound_ForCrossTenantNote()
    {
        ActAs(ClubBMemberId, ClubBId);
        var sut = CreateService();

        var result = await sut.EditAsync(new EditEvaluationNoteInput
        {
            NoteId = _existingNoteId, // belongs to Club A
            Content = "Cross-tenant edit."
        }, TestContext.Current.CancellationToken);

        result.IsT2.ShouldBeTrue(); // NotFound
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    /// <summary>Verifies that the original author can delete their own note.</summary>
    [Fact]
    public async Task Delete_Succeeds_ForAuthor()
    {
        long noteId = SeedNote(_assignmentId, ClubAId, ClubAMember1Id);
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.DeleteAsync(noteId, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue(); // Success
    }

    /// <summary>Verifies that a club administrator can delete any note in their club.</summary>
    [Fact]
    public async Task Delete_Succeeds_ForClubAdmin()
    {
        long noteId = SeedNote(_assignmentId, ClubAId, ClubAMember1Id);
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var sut = CreateService();

        var result = await sut.DeleteAsync(noteId, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue(); // Success
    }

    /// <summary>Verifies that a non-author, non-admin club member cannot delete the note.</summary>
    [Fact]
    public async Task Delete_ReturnsForbidden_ForNonAuthorNonAdmin()
    {
        long noteId = SeedNote(_assignmentId, ClubAId, ClubAMember1Id);
        ActAs(ClubAMember2Id, ClubAId, isClubAdmin: false);
        var sut = CreateService();

        var result = await sut.DeleteAsync(noteId, TestContext.Current.CancellationToken);

        result.IsT2.ShouldBeTrue(); // LifecycleForbidden
    }

    /// <summary>Verifies that deleting a note on a closed campaign returns a conflict.</summary>
    [Fact]
    public async Task Delete_ReturnsConflict_ForClosedCampaign()
    {
        long noteId = SeedNote(_closedAssignmentId, ClubAId, ClubAMember1Id);
        ActAs(ClubAMember1Id, ClubAId);
        var sut = CreateService();

        var result = await sut.DeleteAsync(noteId, TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue(); // LifecycleConflict
    }

    /// <summary>Verifies that a cross-tenant delete attempt returns NotFound.</summary>
    [Fact]
    public async Task Delete_ReturnsNotFound_ForCrossTenantNote()
    {
        ActAs(ClubBMemberId, ClubBId);
        var sut = CreateService();

        var result = await sut.DeleteAsync(_existingNoteId, TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue(); // NotFound
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
        new(new HarnessDbContextFactory(_harness), _harness.CurrentUser, NullLogger<EvaluationNoteService>.Instance);

    /// <summary>
    /// Seeds clubs, users, players, campaigns, and participations needed for the service tests.
    /// </summary>
    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAMember1Id },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });

        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAMember1Id, FirstName = "Alice", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAMember2Id, FirstName = "Aaron", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bob", LastName = "B", ClubId = ClubBId });

        var player = new PlayerEntity
        {
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
            Name = "Season 2026",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        db.Seasons.Add(season);
        db.SaveChanges();

        var activeCampaign = new CampaignEntity
        {
            Name = "Active Campaign",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAMember1Id
        };
        var closedCampaign = new CampaignEntity
        {
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
