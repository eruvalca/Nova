using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.Shared.Enums;

namespace Nova.Browser.Tests;

/// <summary>
/// The seeded evaluation workspace a browser scenario runs against.
/// </summary>
/// <param name="ClubId">The owning club identifier.</param>
/// <param name="CampaignId">The active campaign identifier.</param>
/// <param name="CampaignName">The active campaign name.</param>
/// <param name="AdminUserId">The club administrator's user identifier.</param>
/// <param name="AdminEmail">The club administrator's login e-mail.</param>
/// <param name="EvaluatorUserId">The approved evaluator's user identifier.</param>
/// <param name="EvaluatorEmail">The approved evaluator's login e-mail.</param>
/// <param name="AssignmentIds">All participant assignment identifiers in seeded order.</param>
/// <param name="ArchivedTagApplicationAssignmentId">The assignment carrying a pre-applied archived tag.</param>
/// <param name="ActiveTagName">An active tag-definition name available for application.</param>
/// <param name="SecondActiveTagName">A second active tag-definition name.</param>
/// <param name="ArchivedTagName">The archived tag-definition name (pre-applied on one participant).</param>
public sealed record SeededEvaluationWorkspace(
    long ClubId,
    long CampaignId,
    string CampaignName,
    long AdminUserId,
    string AdminEmail,
    long EvaluatorUserId,
    string EvaluatorEmail,
    IReadOnlyList<long> AssignmentIds,
    long ArchivedTagApplicationAssignmentId,
    string ActiveTagName,
    string SecondActiveTagName,
    string ArchivedTagName);

/// <summary>
/// Seeds a complete evaluation workspace for browser scenarios: an administrator and an
/// approved evaluator (registered through the real Identity HTTP flow), a club, an active
/// campaign with 60 participants (two roster pages at the default page size of 50), two active
/// tag definitions, and one archived tag definition pre-applied to a participant.
/// </summary>
public static class EvaluationSeed
{
    /// <summary>The password shared by every seeded user.</summary>
    public const string Password = "Test#Passw0rd!";

    /// <summary>The number of participants seeded (two pages at the default page size of 50).</summary>
    public const int ParticipantCount = 60;

    /// <summary>
    /// Seeds the workspace and returns its identifiers plus the seeded user credentials.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded workspace.</returns>
    public static async Task<SeededEvaluationWorkspace> SeedAsync(
        NovaAppHostFixture fixture,
        CancellationToken cancellationToken)
    {
        // Register the club administrator and create the club (the create flow makes them the admin).
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("browser-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken, firstName: "Alice", lastName: "Author");
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        // Register the approved evaluator.
        using var evaluatorClient = fixture.CreateNovaHttpClient();
        var evaluatorEmail = SeedingHelpers.UniqueEmail("browser-evaluator");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(evaluatorClient, evaluatorEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, evaluatorEmail, club.ClubId, cancellationToken, firstName: "Bob", lastName: "Observer");

        long adminUserId;
        long evaluatorUserId;
        await using (var context = fixture.CreateAdminContext())
        {
            adminUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken)).Id;
            evaluatorUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == evaluatorEmail.ToUpperInvariant(), cancellationToken)).Id;
        }

        var (campaignId, campaignName, assignmentIds, archivedApplicationAssignmentId, activeTagName, secondActiveTagName, archivedTagName) =
            await SeedWorkspaceDataAsync(fixture, club.ClubId, adminEmail, adminUserId, cancellationToken);

        return new SeededEvaluationWorkspace(
            club.ClubId,
            campaignId,
            campaignName,
            adminUserId,
            adminEmail,
            evaluatorUserId,
            evaluatorEmail,
            assignmentIds,
            archivedApplicationAssignmentId,
            activeTagName,
            secondActiveTagName,
            archivedTagName);
    }

    /// <summary>
    /// Seeds the season, campaign, participants, and tag definitions directly through the admin
    /// context. Every participant has a final outcome (Not selected) so the campaign can be
    /// closed by the stale-close scenario.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminEmail">The administrator's e-mail address used for created-by stamping.</param>
    /// <param name="adminUserId">The administrator's user identifier used for the archived application.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The campaign identifier, participant identifiers, archived-application assignment, and tag names.</returns>
    private static async Task<(
        long CampaignId,
        string CampaignName,
        IReadOnlyList<long> AssignmentIds,
        long ArchivedTagApplicationAssignmentId,
        string ActiveTagName,
        string SecondActiveTagName,
        string ArchivedTagName)> SeedWorkspaceDataAsync(
        NovaAppHostFixture fixture,
        long clubId,
        string adminEmail,
        long adminUserId,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var seeded = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture, clubId, adminEmail, "Browser", ParticipantCount, PlacementOutcome.NotSelected, cancellationToken);

        var activeTagName = $"Winger {suffix}";
        var secondActiveTagName = $"Keeper {suffix}";
        var archivedTagName = $"Legacy {suffix}";
        await SeedingHelpers.InsertTagDefinitionAsync(fixture, seeded.AssignmentIds[0], adminEmail, activeTagName, "#00CC00", cancellationToken);
        await SeedingHelpers.InsertTagDefinitionAsync(fixture, seeded.AssignmentIds[0], adminEmail, secondActiveTagName, "#CC0000", cancellationToken);
        var archivedTagId = await SeedingHelpers.InsertTagDefinitionAsync(fixture, seeded.AssignmentIds[0], adminEmail, archivedTagName, "#999999", cancellationToken, archived: true);

        // Pre-apply the archived tag to the first participant so the archived-definition
        // scenario starts from an existing application.
        await using (var context = fixture.CreateAdminContext())
        {
            context.Add(new CampaignTagApplicationEntity
            {
                PlayerCampaignAssignmentId = seeded.AssignmentIds[0],
                PlayerTagId = archivedTagId,
                ClubId = clubId,
                CreatedById = adminUserId
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        return (
            seeded.CampaignId,
            seeded.CampaignName,
            seeded.AssignmentIds,
            seeded.AssignmentIds[0],
            activeTagName,
            secondActiveTagName,
            archivedTagName);
    }
}
