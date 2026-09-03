using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Integration.Tests.Data;
using Nova.Shared.Enums;
using Nova.Shared.Features.Clubs;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Shared seeding primitives for cross-slice integration and browser tests: unique e-mails,
/// club creation, club-membership cookie refresh, user row updates, campaign/participant
/// seeding, and tag-definition insertion. Keeps the registration and seeding flows in one
/// place so identity or club-complete flow changes only need updating here.
/// </summary>
internal static class SeedingHelpers
{
    /// <summary>
    /// Generates a unique e-mail address for a test user.
    /// </summary>
    /// <param name="prefix">A stable prefix included in the address.</param>
    /// <returns>A unique e-mail address.</returns>
    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";

    /// <summary>
    /// Creates a club through the real HTTP endpoint and returns the club DTO.
    /// </summary>
    /// <param name="client">The authenticated HTTP client.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The created club.</returns>
    public static async Task<ClubDto> CreateClubAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            ClubEndpoints.Create,
            CreateClubMultipartContent($"Club {Guid.NewGuid():N}", "X", "TX"),
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken))!;
    }

    /// <summary>
    /// Builds the multipart form content for a club creation request, including a small
    /// valid crest JPEG. Uses the same field names as the server handler
    /// (<c>name</c>, <c>city</c>, <c>state</c>, <c>crest</c>).
    /// </summary>
    /// <param name="name">The club display name.</param>
    /// <param name="city">The club city.</param>
    /// <param name="state">The club state.</param>
    /// <returns>The multipart form content.</returns>
    public static MultipartFormDataContent CreateClubMultipartContent(string name, string city, string state)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(name), "name");
        form.Add(new StringContent(city), "city");
        form.Add(new StringContent(state), "state");
        var crestBytes = CreateJpegBytes();
        var crestContent = new ByteArrayContent(crestBytes);
        crestContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(crestContent, "crest", "crest");
        return form;
    }

    /// <summary>
    /// Generates a small valid JPEG for crest (and general image) uploads.
    /// </summary>
    /// <returns>The encoded JPEG bytes.</returns>
    public static byte[] CreateJpegBytes(int width = 64, int height = 64)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(120, 180, 240));
        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder());
        return stream.ToArray();
    }

    /// <summary>
    /// Completes the club-membership flow so the client carries the refreshed membership cookie.
    /// </summary>
    /// <param name="client">The HTTP client whose membership cookie should be refreshed.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the cookie has been refreshed.</returns>
    public static async Task RefreshClubMembershipCookieAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"{ClubEndpoints.Complete}?returnUrl=/dashboard", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
    }

    /// <summary>
    /// Updates a user's club membership and optional display name directly in the database.
    /// </summary>
    /// <param name="fixture">The AppHost fixture providing the admin context.</param>
    /// <param name="email">The user's e-mail address.</param>
    /// <param name="clubId">The club identifier to assign, or <see langword="null"/> to clear membership.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <param name="firstName">The optional first name to assign.</param>
    /// <param name="lastName">The optional last name to assign.</param>
    /// <returns>A task that completes when the user has been updated.</returns>
    public static async Task UpdateUserAsync(
        NovaAppHostFixture fixture,
        string email,
        long? clubId,
        CancellationToken cancellationToken,
        string? firstName = null,
        string? lastName = null)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        user.ClubId = clubId;
        if (firstName is not null)
        {
            user.FirstName = firstName;
        }

        if (lastName is not null)
        {
            user.LastName = lastName;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The identifiers produced by campaign seeding.
    /// </summary>
    /// <param name="CampaignId">The campaign identifier.</param>
    /// <param name="CampaignName">The campaign name.</param>
    /// <param name="AssignmentIds">The participant assignment identifiers in seeded (tryout) order.</param>
    public sealed record SeededCampaign(long CampaignId, string CampaignName, IReadOnlyList<long> AssignmentIds);

    /// <summary>
    /// Seeds an active season, an active campaign, and the requested number of players with
    /// their campaign participation rows for the given club.
    /// </summary>
    /// <param name="fixture">The AppHost fixture providing the admin context.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminEmail">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="namePrefix">A stable name prefix for the seeded records.</param>
    /// <param name="participantCount">The number of players to seed.</param>
    /// <param name="placementOutcome">The placement outcome applied to every seeded assignment.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded campaign identifiers.</returns>
    public static async Task<SeededCampaign> SeedCampaignWithParticipantsAsync(
        NovaAppHostFixture fixture,
        long clubId,
        string adminEmail,
        string namePrefix,
        int participantCount,
        PlacementOutcome placementOutcome,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(candidate => candidate.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var club = await context.Clubs.SingleAsync(
            candidate => candidate.ClubId == clubId,
            cancellationToken);
        var season = club.CurrentSeasonId is long currentSeasonId
            ? await context.Seasons.SingleAsync(
                candidate => candidate.SeasonId == currentSeasonId,
                cancellationToken)
            : new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"{namePrefix} Season {suffix}",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = clubId,
                CreatedById = user.Id
            };
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"{namePrefix} Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            Season = season,
            SeasonId = 0,
            ClubId = clubId,
            CreatedById = user.Id
        };
        if (club.CurrentSeasonId is null)
        {
            context.Add(season);
        }

        context.Add(campaign);
        await context.SaveChangesAsync(cancellationToken);
        if (club.CurrentSeasonId is null)
        {
            club.CurrentSeasonId = season.SeasonId;
            await context.SaveChangesAsync(cancellationToken);
        }

        var players = new List<PlayerEntity>(participantCount);
        for (var index = 1; index <= participantCount; index++)
        {
            players.Add(new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                FirstName = namePrefix,
                LastName = $"Player {index:D2}",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030 + (index % 3),
                LifecycleStatus = LifecycleStatus.Active,
                ClubId = clubId,
                CreatedById = user.Id
            });
        }

        context.AddRange(players);
        await context.SaveChangesAsync(cancellationToken);

        var assignments = players.Select(player => new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = clubId,
            CreatedById = user.Id,
            PlacementOutcome = placementOutcome,
            TryoutNumber = players.IndexOf(player) + 1
        }).ToList();
        context.AddRange(assignments);
        await context.SaveChangesAsync(cancellationToken);

        var assignmentIds = await context.PlayerCampaignAssignments
            .Where(candidate => candidate.CampaignId == campaign.CampaignId)
            .OrderBy(candidate => candidate.TryoutNumber)
            .Select(candidate => candidate.PlayerCampaignAssignmentId)
            .ToListAsync(cancellationToken);

        return new SeededCampaign(campaign.CampaignId, campaign.Name, assignmentIds);
    }

    /// <summary>
    /// The identifiers produced by season/campaign seeding without participants.
    /// </summary>
    /// <param name="SeasonId">The season identifier.</param>
    /// <param name="CampaignId">The active campaign identifier.</param>
    public sealed record SeededSeasonAndCampaign(long SeasonId, long CampaignId);

    /// <summary>
    /// Seeds an active season and an active campaign (no participants) for the given club.
    /// </summary>
    /// <param name="fixture">The AppHost fixture providing the admin context.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminEmail">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="namePrefix">A stable name prefix for the seeded records.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The seeded season and campaign identifiers.</returns>
    public static async Task<SeededSeasonAndCampaign> SeedSeasonAndCampaignAsync(
        NovaAppHostFixture fixture,
        long clubId,
        string adminEmail,
        string namePrefix,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users.SingleAsync(
            candidate => candidate.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken);
        var suffix = Guid.NewGuid().ToString("N");
        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"{namePrefix} Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            ClubId = clubId,
            CreatedById = user.Id
        };
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"{namePrefix} Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            Season = season,
            SeasonId = 0,
            ClubId = clubId,
            CreatedById = user.Id
        };
        context.AddRange(season, campaign);
        await context.SaveChangesAsync(cancellationToken);
        var club = await context.Clubs.SingleAsync(candidate => candidate.ClubId == clubId, cancellationToken);
        if (club.CurrentSeasonId is null)
        {
            club.CurrentSeasonId = season.SeasonId;
            await context.SaveChangesAsync(cancellationToken);
        }

        return new SeededSeasonAndCampaign(season.SeasonId, campaign.CampaignId);
    }

    /// <summary>
    /// Inserts an active team for the given club with the requested graduation-year cutoff.
    /// </summary>
    /// <param name="fixture">The AppHost fixture providing the admin context.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="adminEmail">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="name">The team name.</param>
    /// <param name="graduationYear">The minimum player graduation year eligible for the team.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The new team identifier.</returns>
    public static async Task<long> InsertTeamAsync(
        NovaAppHostFixture fixture,
        long clubId,
        string adminEmail,
        string name,
        int graduationYear,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var user = await context.Users
            .SingleAsync(candidate => candidate.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken);
        var team = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = name,
            GraduationYear = graduationYear,
            ClubId = clubId,
            CreatedById = user.Id
        };
        context.Add(team);
        await context.SaveChangesAsync(cancellationToken);
        return team.TeamId;
    }

    /// <summary>
    /// Assigns an existing campaign participation record to the supplied team and marks its placement
    /// outcome as <see cref="PlacementOutcome.Assigned"/>, so browser scenarios can seed a roster or
    /// placements view that renders the assigned <c>text-bg-success</c> outcome badge.
    /// </summary>
    /// <param name="fixture">The AppHost fixture providing the admin context.</param>
    /// <param name="assignmentId">The participation record to assign.</param>
    /// <param name="teamId">The team identifier to assign the participant to.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the placement has been assigned.</returns>
    public static async Task AssignPlacementAsync(
        NovaAppHostFixture fixture,
        long assignmentId,
        long teamId,
        CancellationToken cancellationToken)
    {
        await using var context = fixture.CreateAdminContext();
        var assignment = await context.PlayerCampaignAssignments
            .SingleAsync(candidate => candidate.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        assignment.PlacementOutcome = PlacementOutcome.Assigned;
        assignment.TeamId = teamId;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts a tag definition for the club that owns the given assignment.
    /// </summary>
    /// <param name="fixture">The AppHost fixture providing the admin context.</param>
    /// <param name="assignmentId">The participation identifier whose club owns the new tag.</param>
    /// <param name="adminEmail">A registered user email whose database row provides the created-by identifier.</param>
    /// <param name="name">The tag name.</param>
    /// <param name="color">The tag color token.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <param name="archived">Whether the tag definition should be seeded as archived.</param>
    /// <returns>The new tag definition identifier.</returns>
    public static async Task<long> InsertTagDefinitionAsync(
        NovaAppHostFixture fixture,
        long assignmentId,
        string adminEmail,
        string name,
        string color,
        CancellationToken cancellationToken,
        bool archived = false)
    {
        await using var context = fixture.CreateAdminContext();
        var assignment = await context.PlayerCampaignAssignments
            .SingleAsync(candidate => candidate.PlayerCampaignAssignmentId == assignmentId, cancellationToken);
        var user = await context.Users
            .SingleAsync(candidate => candidate.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken);
        var playerTag = new PlayerTagEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = name,
            NormalizedName = name.Trim().ToUpperInvariant(),
            Color = color,
            LifecycleStatus = archived ? LifecycleStatus.Archived : LifecycleStatus.Active,
            ArchivedAt = archived ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            ArchivedById = archived ? user.Id : null,
            ClubId = assignment.ClubId,
            CreatedById = user.Id
        };
        context.Add(playerTag);
        await context.SaveChangesAsync(cancellationToken);
        return playerTag.PlayerTagId;
    }

    /// <summary>
    /// Closes a campaign through the real server-side lifecycle service, so tests obtain a genuine
    /// <c>Closed</c> lifecycle event and closure provenance without an HTTP round-trip. The simulated
    /// user is assigned directly on the AsyncLocal-backed provider, which is flow-local and
    /// parallel-safe under <see cref="ParallelMode.All"/>.
    /// </summary>
    /// <param name="fixture">The AppHost fixture providing the tenant context factory.</param>
    /// <param name="clubId">The campaign's club identifier.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the campaign is closed.</returns>
    public static async Task CloseCampaignThroughServiceAsync(
        NovaAppHostFixture fixture,
        long clubId,
        long actorUserId,
        long campaignId,
        CancellationToken cancellationToken)
    {
        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var service = new CampaignLifecycleService(
            fixture.CreateTenantContextFactory(),
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);
        var result = await service.CloseAsync(campaignId, cancellationToken);
        result.IsT0.ShouldBeTrue();
    }

    /// <summary>
    /// Reopens a closed campaign through the real server-side lifecycle service, so tests obtain a
    /// genuine <c>Reopened</c> lifecycle event without an HTTP round-trip.
    /// </summary>
    /// <param name="fixture">The AppHost fixture providing the tenant context factory.</param>
    /// <param name="clubId">The campaign's club identifier.</param>
    /// <param name="actorUserId">The acting administrator identifier.</param>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the campaign is reopened.</returns>
    public static async Task ReopenCampaignThroughServiceAsync(
        NovaAppHostFixture fixture,
        long clubId,
        long actorUserId,
        long campaignId,
        CancellationToken cancellationToken)
    {
        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var service = new CampaignLifecycleService(
            fixture.CreateTenantContextFactory(),
            fixture.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);
        var result = await service.ReopenAsync(campaignId, cancellationToken);
        result.IsT0.ShouldBeTrue();
    }
}
