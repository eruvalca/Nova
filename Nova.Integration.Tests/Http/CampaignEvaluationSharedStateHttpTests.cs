using System.Net;
using System.Net.Http.Json;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Cross-slice HTTP coverage for shared evaluation state: mutations made by one approved club
/// member must be observable by a second approved member with correct actor and timestamp
/// metadata. Per-actor authorization rules are owned by the EvaluationNoteHttpTests and
/// CampaignTagApplicationHttpTests suites; this file only proves the shared-observation slice.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignEvaluationSharedStateHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies a note added by one approved member is visible to a second approved member
    /// through the participant detail payload, carrying the author's display name, a recent
    /// timestamp, and per-caller edit/delete capability flags.
    /// </summary>
    [Fact]
    public async Task SharedEvaluationNote_IsVisibleToSecondApprovedMember_WithAuthorAndTimestampMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (authorClient, observerClient, _, campaignId, assignmentId) = await SeedTwoMemberClubWithCampaignAsync(
            "note-share", cancellationToken);

        using var addResponse = await authorClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            new AddEvaluationNoteInput { PlayerCampaignAssignmentId = assignmentId, Content = "Strong first touch." },
            cancellationToken);
        addResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var detailResponse = await observerClient.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(cancellationToken);
        detail.ShouldNotBeNull();

        var note = detail.Notes.ShouldHaveSingleItem();
        note.Content.ShouldBe("Strong first touch.");
        note.AuthorDisplayName.ShouldBe("Alice Author");
        note.CreatedAt.ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        // The observer is neither the author nor a club administrator.
        note.CanEdit.ShouldBeFalse();
        note.CanDelete.ShouldBeFalse();
    }

    /// <summary>
    /// Verifies a tag applied by one approved member is visible to a second approved member
    /// through the participant detail payload, carrying the applying actor's display name, a
    /// recent timestamp, and a per-caller removal capability flag.
    /// </summary>
    [Fact]
    public async Task SharedTagApplication_IsVisibleToSecondApprovedMember_WithActorAndTimestampMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (authorClient, observerClient, adminEmail, campaignId, assignmentId) = await SeedTwoMemberClubWithCampaignAsync(
            "tag-share", cancellationToken);

        var tagId = await SeedingHelpers.InsertTagDefinitionAsync(fixture, assignmentId, adminEmail, "Striker", "#00CC00", cancellationToken);

        using var applyResponse = await authorClient.PostAsJsonAsync(
            CampaignEndpoints.ApplyCampaignTagApplication,
            new ApplyCampaignTagApplicationInput { PlayerCampaignAssignmentId = assignmentId, PlayerTagId = tagId },
            cancellationToken);
        applyResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var detailResponse = await observerClient.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(cancellationToken);
        detail.ShouldNotBeNull();

        var application = detail.AppliedTags.ShouldHaveSingleItem();
        application.TagName.ShouldBe("Striker");
        application.IsArchived.ShouldBeFalse();
        application.ActorDisplayName.ShouldBe("Alice Author");
        application.AppliedAt.ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        // The observer is neither the applying actor nor a club administrator.
        application.CanRemove.ShouldBeFalse();
    }

    /// <summary>
    /// Verifies independent contributions from two approved members coexist on the same
    /// participant, each carrying its own actor metadata.
    /// </summary>
    [Fact]
    public async Task IndependentContributions_Coexist_WithPerActorMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (authorClient, observerClient, _, campaignId, assignmentId) = await SeedTwoMemberClubWithCampaignAsync(
            "coexist", cancellationToken);

        using var firstAdd = await authorClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            new AddEvaluationNoteInput { PlayerCampaignAssignmentId = assignmentId, Content = "Alice's observation." },
            cancellationToken);
        firstAdd.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var secondAdd = await observerClient.PostAsJsonAsync(
            CampaignEndpoints.AddEvaluationNote,
            new AddEvaluationNoteInput { PlayerCampaignAssignmentId = assignmentId, Content = "Bob's observation." },
            cancellationToken);
        secondAdd.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var detailResponse = await observerClient.GetAsync(
            CampaignEndpoints.GetCampaignParticipantDetailUrl(campaignId, assignmentId),
            cancellationToken);
        detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detail = await detailResponse.Content.ReadFromJsonAsync<CampaignParticipantDetailDto>(cancellationToken);
        detail.ShouldNotBeNull();

        detail.Notes.Count.ShouldBe(2);
        detail.Notes.ShouldContain(note => note.Content == "Alice's observation." && note.AuthorDisplayName == "Alice Author");
        detail.Notes.ShouldContain(note => note.Content == "Bob's observation." && note.AuthorDisplayName == "Bob Observer");
    }

    /// <summary>
    /// Seeds two approved members in one club (author "Alice Author" and observer "Bob Observer"),
    /// an active campaign with one participant, and returns both authenticated clients plus the
    /// campaign/assignment identifiers and the club admin's e-mail (for created-by stamping).
    /// </summary>
    /// <param name="prefix">A stable e-mail prefix.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The author client, observer client, admin e-mail, campaign identifier, and assignment identifier.</returns>
    private async Task<(HttpClient AuthorClient, HttpClient ObserverClient, string AdminEmail, long CampaignId, long AssignmentId)>
        SeedTwoMemberClubWithCampaignAsync(string prefix, CancellationToken cancellationToken)
    {
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail($"{prefix}-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken, firstName: "Admin", lastName: "Creator");
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var authorClient = fixture.CreateNovaHttpClient();
        var authorEmail = SeedingHelpers.UniqueEmail($"{prefix}-author");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(authorClient, authorEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, authorEmail, club.ClubId, cancellationToken, firstName: "Alice", lastName: "Author");
        await SeedingHelpers.RefreshClubMembershipCookieAsync(authorClient, cancellationToken);

        var observerClient = fixture.CreateNovaHttpClient();
        var observerEmail = SeedingHelpers.UniqueEmail($"{prefix}-observer");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(observerClient, observerEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, observerEmail, club.ClubId, cancellationToken, firstName: "Bob", lastName: "Observer");
        await SeedingHelpers.RefreshClubMembershipCookieAsync(observerClient, cancellationToken);

        var seeded = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture, club.ClubId, adminEmail, prefix, participantCount: 1, placementOutcome: PlacementOutcome.Undecided, cancellationToken);
        return (authorClient, observerClient, adminEmail, seeded.CampaignId, seeded.AssignmentIds[0]);
    }
}
