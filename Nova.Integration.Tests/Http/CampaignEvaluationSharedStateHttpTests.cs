using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
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

        var tagId = await InsertTagDefinitionAsync(assignmentId, "Striker", adminEmail, cancellationToken);

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
        var adminEmail = UniqueEmail($"{prefix}-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await UpdateUserAsync(adminEmail, clubId: null, firstName: "Admin", lastName: "Creator", cancellationToken);
        var club = await CreateClubAsync(adminClient, cancellationToken);
        await RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        var authorClient = fixture.CreateNovaHttpClient();
        var authorEmail = UniqueEmail($"{prefix}-author");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(authorClient, authorEmail, Password, cancellationToken);
        await UpdateUserAsync(authorEmail, club.ClubId, firstName: "Alice", lastName: "Author", cancellationToken);
        await RefreshClubMembershipCookieAsync(authorClient, cancellationToken);

        var observerClient = fixture.CreateNovaHttpClient();
        var observerEmail = UniqueEmail($"{prefix}-observer");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(observerClient, observerEmail, Password, cancellationToken);
        await UpdateUserAsync(observerEmail, club.ClubId, firstName: "Bob", lastName: "Observer", cancellationToken);
        await RefreshClubMembershipCookieAsync(observerClient, cancellationToken);

        var (campaignId, assignmentId) = await SeedCampaignDataAsync(club.ClubId, adminEmail, prefix, cancellationToken);
        return (authorClient, observerClient, adminEmail, campaignId, assignmentId);
    }

    /// <summary>
    /// Seeds an active season, campaign, player, and participation for the given club.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="email">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="prefix">A stable name prefix.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The campaign and participation identifiers.</returns>
    private async Task<(long CampaignId, long AssignmentId)> SeedCampaignDataAsync(
        long clubId,
        string email,
        string prefix,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var season = new SeasonEntity { Name = $"{prefix} Season {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = user.Id };
        var campaign = new CampaignEntity
        {
            Name = $"{prefix} Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            Season = season,
            SeasonId = 0,
            ClubId = clubId,
            CreatedById = user.Id
        };
        var player = new PlayerEntity
        {
            FirstName = prefix,
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
    /// Inserts an active tag definition for the club that owns the given assignment.
    /// </summary>
    /// <param name="assignmentId">The participation identifier whose club owns the new tag.</param>
    /// <param name="name">The tag name.</param>
    /// <param name="email">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The new tag definition identifier.</returns>
    private async Task<long> InsertTagDefinitionAsync(long assignmentId, string name, string email, CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var assignment = await context.PlayerCampaignAssignments
            .SingleAsync(candidate => candidate.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        var user = await context.Users
            .SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        var playerTag = new PlayerTagEntity
        {
            Name = name,
            Color = "#00CC00",
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = assignment.ClubId,
            CreatedById = user.Id
        };
        context.Add(playerTag);
        await context.SaveChangesAsync(cancellationToken);
        return playerTag.PlayerTagId;
    }

    /// <summary>
    /// Generates a unique e-mail address for a test user.
    /// </summary>
    /// <param name="prefix">A stable prefix included in the address.</param>
    /// <returns>A unique e-mail address.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

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
    /// Updates a user's club membership and display name directly in the database.
    /// </summary>
    /// <param name="email">The user's e-mail address.</param>
    /// <param name="clubId">The club identifier to assign, or <see langword="null"/> to clear membership.</param>
    /// <param name="firstName">The first name to assign.</param>
    /// <param name="lastName">The last name to assign.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the user has been updated.</returns>
    private async Task UpdateUserAsync(
        string email,
        long? clubId,
        string firstName,
        string lastName,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        user.ClubId = clubId;
        user.FirstName = firstName;
        user.LastName = lastName;
        await context.SaveChangesAsync(cancellationToken);
    }
}
