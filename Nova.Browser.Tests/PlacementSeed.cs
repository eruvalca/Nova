using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.Shared.Enums;

namespace Nova.Browser.Tests;

/// <summary>
/// The seeded placement workspace a browser scenario runs against.
/// </summary>
/// <param name="ClubId">The owning club identifier.</param>
/// <param name="CampaignId">The active campaign identifier.</param>
/// <param name="ClosedCampaignId">The closed campaign identifier.</param>
/// <param name="AdminUserId">The club administrator's user identifier.</param>
/// <param name="AdminEmail">The club administrator's login e-mail.</param>
/// <param name="EvaluatorUserId">The approved evaluator's user identifier.</param>
/// <param name="EvaluatorEmail">The approved evaluator's login e-mail.</param>
/// <param name="EligibleTeamId">An active team eligible for the youngest seeded player.</param>
/// <param name="EligibleTeamName">The eligible team's display name (with cutoff suffix).</param>
/// <param name="IneligibleTeamName">An active team whose cutoff exceeds every seeded graduation year.</param>
public sealed record SeededPlacementWorkspace(
    long ClubId,
    long CampaignId,
    long ClosedCampaignId,
    long AdminUserId,
    string AdminEmail,
    long EvaluatorUserId,
    string EvaluatorEmail,
    long EligibleTeamId,
    string EligibleTeamName,
    string IneligibleTeamName);

/// <summary>
/// Seeds a complete placement workspace for browser scenarios: an administrator and an approved
/// evaluator (registered through the real Identity HTTP flow), a club, an active campaign with 60
/// Undecided participants (two roster pages at the default page size of 50), four active teams with
/// different graduation-year cutoffs, and a closed campaign with final placements.
/// </summary>
public static class PlacementSeed
{
    /// <summary>The password shared by every seeded user.</summary>
    public const string Password = "Test#Passw0rd!";

    /// <summary>The number of participants seeded in the active campaign.</summary>
    public const int ParticipantCount = 60;

    /// <summary>
    /// Seeds the placement workspace and returns its identifiers plus the seeded user credentials.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded placement workspace.</returns>
    public static async Task<SeededPlacementWorkspace> SeedAsync(
        NovaAppHostFixture fixture,
        CancellationToken cancellationToken)
    {
        // Register the club administrator and create the club (the create flow makes them the admin).
        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("placement-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken, firstName: "Alice", lastName: "Author");
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        // Register the approved evaluator.
        using var evaluatorClient = fixture.CreateNovaHttpClient();
        var evaluatorEmail = SeedingHelpers.UniqueEmail("placement-evaluator");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(evaluatorClient, evaluatorEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, evaluatorEmail, club.ClubId, cancellationToken, firstName: "Bob", lastName: "Observer");

        long adminUserId;
        long evaluatorUserId;
        await using (var context = fixture.CreateAdminContext())
        {
            adminUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken)).Id;
            evaluatorUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == evaluatorEmail.ToUpperInvariant(), cancellationToken)).Id;
        }

        // Active campaign: 60 Undecided participants across two pages.
        var active = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture, club.ClubId, adminEmail, "Placement", ParticipantCount, PlacementOutcome.Undecided, cancellationToken);

        // Teams with ascending graduation-year cutoffs: Alpha/Bravo are eligible for most players,
        // Charlie only for 2032 players, Delta is ineligible for everyone.
        var suffix = Guid.NewGuid().ToString("N");
        var eligibleTeamName = $"Alpha {suffix}";
        var ineligibleTeamName = $"Delta {suffix}";
        var eligibleTeamId = await SeedingHelpers.InsertTeamAsync(fixture, club.ClubId, adminEmail, eligibleTeamName, 2028, cancellationToken);
        await SeedingHelpers.InsertTeamAsync(fixture, club.ClubId, adminEmail, $"Bravo {suffix}", 2030, cancellationToken);
        await SeedingHelpers.InsertTeamAsync(fixture, club.ClubId, adminEmail, $"Charlie {suffix}", 2032, cancellationToken);
        await SeedingHelpers.InsertTeamAsync(fixture, club.ClubId, adminEmail, ineligibleTeamName, 2033, cancellationToken);

        // Closed campaign: final placements and a Closed lifecycle status.
        var closed = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture, club.ClubId, adminEmail, "Closed", 3, PlacementOutcome.NotSelected, cancellationToken);
        await using (var context = fixture.CreateAdminContext())
        {
            var campaign = await context.Campaigns.SingleAsync(candidate => candidate.CampaignId == closed.CampaignId, cancellationToken);
            campaign.Status = CampaignStatus.Closed;
            campaign.ClosedAt = DateTimeOffset.UtcNow.AddDays(-1);
            campaign.ClosedById = adminUserId;
            await context.SaveChangesAsync(cancellationToken);
        }

        return new SeededPlacementWorkspace(
            club.ClubId,
            active.CampaignId,
            closed.CampaignId,
            adminUserId,
            adminEmail,
            evaluatorUserId,
            evaluatorEmail,
            eligibleTeamId,
            eligibleTeamName,
            ineligibleTeamName);
    }
}
