using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP coverage for the campaign evaluation note add, edit, and delete endpoints.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class EvaluationNoteHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies anonymous callers receive an unauthorized response for all three note mutations.
    /// </summary>
    [Fact]
    public async Task EvaluationNoteMutations_ReturnUnauthorized_ForAnonymousCaller()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var anonymousClient = fixture.CreateNovaHttpClient();

        using var addResponse = await anonymousClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(1, "Note"),
            cancellationToken);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var editResponse = await anonymousClient.PutAsJsonAsync(
            CampaignEndpoints.EditEvaluationNoteUrl(42),
            ValidEditInput("Note"),
            cancellationToken);
        editResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var deleteResponse = await anonymousClient.DeleteAsync(
            CampaignEndpoints.DeleteEvaluationNoteUrl(42),
            cancellationToken);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies authenticated callers without a club receive forbidden responses for all three note mutations.
    /// </summary>
    [Fact]
    public async Task EvaluationNoteMutations_ReturnForbidden_ForAuthenticatedUserWithoutClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("note-no-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var addResponse = await client.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(1, "Note"),
            cancellationToken);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var editResponse = await client.PutAsJsonAsync(
            CampaignEndpoints.EditEvaluationNoteUrl(42),
            ValidEditInput("Note"),
            cancellationToken);
        editResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var deleteResponse = await client.DeleteAsync(
            CampaignEndpoints.DeleteEvaluationNoteUrl(42),
            cancellationToken);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a least-privileged club member can add a note and the row is persisted with the member
    /// as creator, then the note is reflected in the participant detail payload.
    /// </summary>
    [Fact]
    public async Task AddEvaluationNote_ReturnsCreated_AndPersistsRow_ForLeastPrivilegedClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // The club creator becomes a ClubAdmin by the create-club flow, so a second
        // non-admin user is required to prove ordinary member add access.
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("note-add-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("note-add-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);
        var (campaignId, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, adminEmail, cancellationToken);

        using var addResponse = await memberClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(assignmentId, "Promising footwork and vision."),
            cancellationToken);

        addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var success = await addResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(cancellationToken);
        success.NoteId.ShouldBeGreaterThan(0);

        await using var context = fixture.CreateAdminContext();
        var member = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == memberEmail.ToUpperInvariant(), cancellationToken);
        var persisted = await context.Notes
            .SingleOrDefaultAsync(candidate => candidate.NoteId == success.NoteId, cancellationToken);
        persisted.ShouldNotBeNull();
        persisted.Content.ShouldBe("Promising footwork and vision.");
        persisted.PlayerCampaignAssignmentId.ShouldBe(assignmentId);
        persisted.ClubId.ShouldBe(club.ClubId);
        persisted.CreatedById.ShouldBe(member.Id);

        using var detailResponse = await memberClient.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(cancellationToken);
        detail.ShouldNotBeNull();
        detail.Capabilities.CanAddNote.ShouldBeTrue();
        var note = detail.Notes.ShouldHaveSingleItem();
        note.NoteId.ShouldBe(success.NoteId);
        note.Content.ShouldBe("Promising footwork and vision.");
        note.AuthorDisplayName.ShouldBe("Test User");
        note.CanEdit.ShouldBeTrue();
        note.CanDelete.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies blank note content is rejected with validation ProblemDetails naming the content field.
    /// </summary>
    [Fact]
    public async Task AddEvaluationNote_ReturnsValidationProblem_ForBlankContent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("note-add-validation");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (_, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, email, cancellationToken);

        using var response = await client.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(assignmentId, "   "),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await ReadErrorsAsync(response, cancellationToken);
        errors.ShouldContainKey(nameof(AddEvaluationNoteInput.Content));
    }

    /// <summary>
    /// Verifies adding a note to a participation in a closed campaign returns a conflict.
    /// </summary>
    [Fact]
    public async Task AddEvaluationNote_ReturnsConflict_ForClosedCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("note-add-closed");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (_, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, email, cancellationToken, closedCampaign: true);

        using var response = await client.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(assignmentId, "Note on a closed campaign."),
            cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Closed campaigns are read-only and cannot accept new notes.");
    }

    /// <summary>
    /// Verifies cross-tenant and nonexistent participation identifiers return non-disclosing not-found responses.
    /// </summary>
    [Fact]
    public async Task AddEvaluationNote_ReturnsNotFound_ForCrossTenantAndMissingAssignments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var currentClient = fixture.CreateNovaHttpClient();
        var currentEmail = UniqueEmail("note-add-current");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(currentClient, currentEmail, Password, cancellationToken);
        await UpdateUserAsync(currentEmail, clubId: null, cancellationToken);
        var currentClub = await CreateClubAsync(currentClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(currentClient, cancellationToken);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("note-add-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        var (_, otherAssignmentId) = await SeedEvaluationNoteDataAsync(otherClub.ClubId, otherEmail, cancellationToken);

        // Cross-tenant assignment must be non-disclosing.
        using var crossTenantResponse = await currentClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(otherAssignmentId, "Note"),
            cancellationToken);
        crossTenantResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Nonexistent assignment must also be non-disclosing.
        using var missingResponse = await currentClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(999_999, "Note"),
            cancellationToken);
        missingResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        currentClub.ClubId.ShouldNotBe(otherClub.ClubId);
    }

    /// <summary>
    /// Verifies the note author can edit their note and the row is updated with audit stamps preserved.
    /// </summary>
    [Fact]
    public async Task EditEvaluationNote_ReturnsNoContent_AndUpdatesRow_ForAuthor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("note-edit-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("note-edit-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);
        var (campaignId, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, adminEmail, cancellationToken);

        using var addResponse = await memberClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(assignmentId, "Original content."),
            cancellationToken);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var added = await addResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(cancellationToken);

        using var editResponse = await memberClient.PutAsJsonAsync(
            CampaignEndpoints.EditEvaluationNoteUrl(added.NoteId),
            ValidEditInput("Revised content."),
            cancellationToken);
        editResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var context = fixture.CreateAdminContext();
        var member = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == memberEmail.ToUpperInvariant(), cancellationToken);
        var persisted = await context.Notes
            .SingleOrDefaultAsync(candidate => candidate.NoteId == added.NoteId, cancellationToken);
        persisted.ShouldNotBeNull();
        persisted.Content.ShouldBe("Revised content.");
        persisted.CreatedById.ShouldBe(member.Id);
        persisted.ModifiedById.ShouldBe(member.Id);
        persisted.ModifiedAt.ShouldNotBeNull();

        using var detailResponse = await memberClient.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(cancellationToken);
        detail.ShouldNotBeNull();
        var note = detail.Notes.ShouldHaveSingleItem();
        note.Content.ShouldBe("Revised content.");
        note.AuthorDisplayName.ShouldBe("Test User");
        note.ModifiedAt.ShouldNotBeNull();
        note.ModifiedAt.Value.ShouldBeGreaterThanOrEqualTo(note.CreatedAt);
        note.CanEdit.ShouldBeTrue();
        note.CanDelete.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies a non-author, non-admin club member cannot edit another member's note.
    /// </summary>
    [Fact]
    public async Task EditEvaluationNote_ReturnsForbidden_ForNonAuthorNonAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("note-edit-forbidden-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var ownerClient = fixture.CreateNovaHttpClient();
        var ownerEmail = UniqueEmail("note-edit-forbidden-owner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(ownerClient, ownerEmail, Password, cancellationToken);
        await UpdateUserAsync(ownerEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(ownerClient, cancellationToken);
        var (_, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, adminEmail, cancellationToken);

        using var addResponse = await ownerClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(assignmentId, "Owner content."),
            cancellationToken);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var added = await addResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(cancellationToken);

        using var otherMemberClient = fixture.CreateNovaHttpClient();
        var otherMemberEmail = UniqueEmail("note-edit-forbidden-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherMemberClient, otherMemberEmail, Password, cancellationToken);
        await UpdateUserAsync(otherMemberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherMemberClient, cancellationToken);

        using var editResponse = await otherMemberClient.PutAsJsonAsync(
            CampaignEndpoints.EditEvaluationNoteUrl(added.NoteId),
            ValidEditInput("Sneaky edit."),
            cancellationToken);

        editResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var document = await JsonDocument.ParseAsync(
            await editResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Only the note author or a club administrator may edit evaluation notes.");
        await AssertNotePersistedAsync(added.NoteId, "Owner content.", cancellationToken);
    }

    /// <summary>
    /// Verifies editing a note in a closed campaign returns a conflict and leaves the row intact.
    /// </summary>
    [Fact]
    public async Task EditEvaluationNote_ReturnsConflict_ForClosedCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("note-edit-closed");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (_, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, email, cancellationToken, closedCampaign: true);
        var noteId = await InsertNoteAsync(club.ClubId, assignmentId, email, "Pre-existing note.", cancellationToken);

        using var editResponse = await client.PutAsJsonAsync(
            CampaignEndpoints.EditEvaluationNoteUrl(noteId),
            ValidEditInput("Attempted edit."),
            cancellationToken);

        editResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await editResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Closed campaigns are read-only and cannot accept note edits.");
        await AssertNotePersistedAsync(noteId, "Pre-existing note.", cancellationToken);
    }

    /// <summary>
    /// Verifies editing another club's note identifier is non-disclosing and leaves the row intact.
    /// </summary>
    [Fact]
    public async Task EditEvaluationNote_ReturnsNotFound_ForCrossTenantNote()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var ownerClient = fixture.CreateNovaHttpClient();
        var ownerEmail = UniqueEmail("note-edit-owner-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(ownerClient, ownerEmail, Password, cancellationToken);
        await UpdateUserAsync(ownerEmail, clubId: null, cancellationToken);
        var ownerClub = await CreateClubAsync(ownerClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(ownerClient, cancellationToken);
        var (_, assignmentId) = await SeedEvaluationNoteDataAsync(ownerClub.ClubId, ownerEmail, cancellationToken);

        using var addResponse = await ownerClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(assignmentId, "Owner note."),
            cancellationToken);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var added = await addResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(cancellationToken);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("note-edit-other-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        otherClub.ClubId.ShouldNotBe(ownerClub.ClubId);

        using var editResponse = await otherClient.PutAsJsonAsync(
            CampaignEndpoints.EditEvaluationNoteUrl(added.NoteId),
            ValidEditInput("Cross-tenant edit."),
            cancellationToken);

        editResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await AssertNotePersistedAsync(added.NoteId, "Owner note.", cancellationToken);
    }

    /// <summary>
    /// Verifies the note author can delete their note and the row is deleted from the database and detail payload.
    /// </summary>
    [Fact]
    public async Task DeleteEvaluationNote_ReturnsNoContent_AndDeletesRow_ForAuthor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // A second non-admin user proves ordinary author delete access (club creator is ClubAdmin).
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("note-delete-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("note-delete-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await UpdateUserAsync(memberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(memberClient, cancellationToken);
        var (campaignId, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, adminEmail, cancellationToken);

        using var addResponse = await memberClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(assignmentId, "Note to delete."),
            cancellationToken);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var added = await addResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(cancellationToken);

        using var deleteResponse = await memberClient.DeleteAsync(
            CampaignEndpoints.DeleteEvaluationNoteUrl(added.NoteId),
            cancellationToken);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var context = fixture.CreateAdminContext();
        var persisted = await context.Notes
            .SingleOrDefaultAsync(candidate => candidate.NoteId == added.NoteId, cancellationToken);
        persisted.ShouldBeNull();

        using var detailResponse = await memberClient.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(cancellationToken);
        detail.ShouldNotBeNull();
        detail.Notes.ShouldNotContain(note => note.NoteId == added.NoteId);
    }

    /// <summary>
    /// Verifies a non-author, non-admin club member cannot delete another member's note.
    /// </summary>
    [Fact]
    public async Task DeleteEvaluationNote_ReturnsForbidden_ForNonAuthorNonAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = UniqueEmail("note-delete-forbidden-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var ownerClient = fixture.CreateNovaHttpClient();
        var ownerEmail = UniqueEmail("note-delete-forbidden-owner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(ownerClient, ownerEmail, Password, cancellationToken);
        await UpdateUserAsync(ownerEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(ownerClient, cancellationToken);
        var (_, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, adminEmail, cancellationToken);

        using var addResponse = await ownerClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(assignmentId, "Owner note."),
            cancellationToken);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var added = await addResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(cancellationToken);

        using var otherMemberClient = fixture.CreateNovaHttpClient();
        var otherMemberEmail = UniqueEmail("note-delete-forbidden-other");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherMemberClient, otherMemberEmail, Password, cancellationToken);
        await UpdateUserAsync(otherMemberEmail, club.ClubId, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherMemberClient, cancellationToken);

        using var deleteResponse = await otherMemberClient.DeleteAsync(
            CampaignEndpoints.DeleteEvaluationNoteUrl(added.NoteId),
            cancellationToken);

        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var document = await JsonDocument.ParseAsync(
            await deleteResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Only the note author or a club administrator may delete evaluation notes.");
        await AssertNotePersistedAsync(added.NoteId, "Owner note.", cancellationToken);
    }

    /// <summary>
    /// Verifies deleting a note in a closed campaign returns a conflict and leaves the row intact.
    /// </summary>
    [Fact]
    public async Task DeleteEvaluationNote_ReturnsConflict_ForClosedCampaign()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("note-delete-closed");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (_, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, email, cancellationToken, closedCampaign: true);
        var noteId = await InsertNoteAsync(club.ClubId, assignmentId, email, "Pre-existing note.", cancellationToken);

        using var deleteResponse = await client.DeleteAsync(
            CampaignEndpoints.DeleteEvaluationNoteUrl(noteId),
            cancellationToken);

        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var document = await JsonDocument.ParseAsync(
            await deleteResponse.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        document.RootElement.GetProperty("detail").GetString()
            .ShouldBe("Closed campaigns are read-only and cannot accept note deletions.");
        await AssertNotePersistedAsync(noteId, "Pre-existing note.", cancellationToken);
    }

    /// <summary>
    /// Verifies deleting another club's note identifier is non-disclosing and leaves the row intact.
    /// </summary>
    [Fact]
    public async Task DeleteEvaluationNote_ReturnsNotFound_ForCrossTenantNote()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var ownerClient = fixture.CreateNovaHttpClient();
        var ownerEmail = UniqueEmail("note-delete-owner-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(ownerClient, ownerEmail, Password, cancellationToken);
        await UpdateUserAsync(ownerEmail, clubId: null, cancellationToken);
        var ownerClub = await CreateClubAsync(ownerClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(ownerClient, cancellationToken);
        var (_, assignmentId) = await SeedEvaluationNoteDataAsync(ownerClub.ClubId, ownerEmail, cancellationToken);

        using var addResponse = await ownerClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(assignmentId, "Owner note."),
            cancellationToken);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var added = await addResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(cancellationToken);

        using var otherClient = fixture.CreateNovaHttpClient();
        var otherEmail = UniqueEmail("note-delete-other-club");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(otherClient, otherEmail, Password, cancellationToken);
        await UpdateUserAsync(otherEmail, clubId: null, cancellationToken);
        var otherClub = await CreateClubAsync(otherClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(otherClient, cancellationToken);
        otherClub.ClubId.ShouldNotBe(ownerClub.ClubId);

        using var deleteResponse = await otherClient.DeleteAsync(
            CampaignEndpoints.DeleteEvaluationNoteUrl(added.NoteId),
            cancellationToken);

        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await AssertNotePersistedAsync(added.NoteId, "Owner note.", cancellationToken);
    }

    /// <summary>
    /// Verifies a successful add is reflected in the participant detail payload the notes drawer consumes.
    /// </summary>
    [Fact]
    public async Task AddEvaluationNote_IsReflected_InParticipantDetailNotes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("note-refresh-story");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);
        await UpdateUserAsync(email, clubId: null, cancellationToken);
        var club = await CreateClubAsync(client, cancellationToken);
        await RefreshClubMembershipCookieAsync(client, cancellationToken);
        var (campaignId, assignmentId) = await SeedEvaluationNoteDataAsync(club.ClubId, email, cancellationToken);

        using var addResponse = await client.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            ValidAddInput(assignmentId, "Reflected note."),
            cancellationToken);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var added = await addResponse.Content.ReadFromJsonAsync<EvaluationNoteMutationSuccess>(cancellationToken);

        using var detailResponse = await client.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(cancellationToken);
        detail.ShouldNotBeNull();
        detail.Notes.ShouldContain(
            note => note.NoteId == added.NoteId
                && note.Content == "Reflected note."
                && note.AuthorDisplayName == "Test User");
    }

    /// <summary>
    /// Generates a unique e-mail address for a test user.
    /// </summary>
    /// <param name="prefix">A stable prefix included in the address.</param>
    /// <returns>A unique e-mail address.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    /// <summary>
    /// Creates a valid add-note request.
    /// </summary>
    /// <param name="assignmentId">The player campaign assignment the note is attached to.</param>
    /// <param name="content">The note content.</param>
    /// <returns>A valid request for server serialization.</returns>
    private static AddEvaluationNoteInput ValidAddInput(long assignmentId, string content)
        => new() { PlayerCampaignAssignmentId = assignmentId, Content = content };

    /// <summary>
    /// Creates a valid edit-note request body.
    /// </summary>
    /// <param name="content">The note content.</param>
    /// <returns>A valid request body for server serialization.</returns>
    private static PutEvaluationNoteInput ValidEditInput(string content)
        => new() { Content = content };

    /// <summary>
    /// Creates a club and returns the resulting club DTO.
    /// </summary>
    /// <param name="client">The HTTP client used to create the club.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club.</returns>
    private static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            ClubEndpoints.Create,
            new CreateClubInput { Name = $"Club {Guid.NewGuid():N}", City = "X", State = "TX" },
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    /// <summary>
    /// Completes the club-membership flow so the client carries the refreshed membership cookie.
    /// </summary>
    /// <param name="client">The HTTP client whose membership cookie should be refreshed.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the cookie has been refreshed.</returns>
    private static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>
    /// Updates a user's club membership directly in the database.
    /// </summary>
    /// <param name="email">The user's e-mail address.</param>
    /// <param name="clubId">The club identifier to assign, or <see langword="null"/> to clear membership.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the user has been updated.</returns>
    private async Task UpdateUserAsync(string email, long? clubId, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        user.ClubId = clubId;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds an active season, campaign, player, and participation for the given club.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="email">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <param name="closedCampaign">Whether the campaign should be seeded as closed.</param>
    /// <returns>The campaign and participation identifiers.</returns>
    private async Task<(long CampaignId, long AssignmentId)> SeedEvaluationNoteDataAsync(
        long clubId,
        string email,
        CancellationToken cancellationToken,
        bool closedCampaign = false)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var season = new SeasonEntity { Name = $"Note Season {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = user.Id };
        var campaign = new CampaignEntity
        {
            Name = $"Note Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = closedCampaign ? CampaignStatus.Closed : CampaignStatus.Active,
            ClosedAt = closedCampaign ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            ClosedById = closedCampaign ? user.Id : null,
            Season = season,
            SeasonId = 0,
            ClubId = clubId,
            CreatedById = user.Id
        };
        var player = new PlayerEntity
        {
            FirstName = "Note",
            LastName = $"Player {suffix}",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = user.Id
        };

        context.AddRange(season, campaign, player);
        await context.SaveChangesAsync(cancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = clubId,
            CreatedById = user.Id,
            PlacementOutcome = PlacementOutcome.Undecided,
            TryoutNumber = 7
        };
        context.Add(assignment);
        await context.SaveChangesAsync(cancellationToken);

        return (campaign.CampaignId, assignment.PlayerCampaignAssignmentId);
    }

    /// <summary>
    /// Inserts an evaluation note row directly via the admin context, bypassing the add endpoint's
    /// lifecycle guards so boundary tests can start from a closed campaign.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="assignmentId">The participation identifier.</param>
    /// <param name="email">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="content">The note content.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The new note identifier.</returns>
    private async Task<long> InsertNoteAsync(
        long clubId,
        long assignmentId,
        string email,
        string content,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        var note = new NoteEntity
        {
            Content = content,
            PlayerCampaignAssignmentId = assignmentId,
            ClubId = clubId,
            CreatedById = user.Id
        };
        context.Add(note);
        await context.SaveChangesAsync(cancellationToken);
        return note.NoteId;
    }

    /// <summary>
    /// Asserts the given note row still exists with the expected content after a rejected mutation attempt.
    /// </summary>
    /// <param name="noteId">The note identifier.</param>
    /// <param name="expectedContent">The content the row should still hold.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    private async Task AssertNotePersistedAsync(long noteId, string expectedContent, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var persisted = await context.Notes
            .SingleOrDefaultAsync(candidate => candidate.NoteId == noteId, cancellationToken);
        persisted.ShouldNotBeNull();
        persisted.Content.ShouldBe(expectedContent);
    }

    /// <summary>
    /// Reads the <c>errors</c> dictionary from a validation ProblemDetails payload.
    /// </summary>
    /// <param name="response">The problem-details response.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The validation error dictionary.</returns>
    private static async Task<Dictionary<string, string[]>> ReadErrorsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var errors = document.RootElement.GetProperty("errors");
        return errors.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray());
    }
}
