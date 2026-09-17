using System.Net.Http.Json;
using Nova.SharedKernel.Features.Players;
using Shouldly;

namespace Nova.Integration.Tests.Http;

public sealed partial class PlayerRosterHttpTests
{
    [Fact]
    public async Task SummarySerializesClubWideCountsForOrdinaryMemberAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateNovaHttpClient();
        var email = UniqueEmail("directory-summary-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(admin, email, Password, ct);
        await UpdateUserAsync(email, "Alex", "Admin", null, ct);
        var club = await CreateClubAsync(admin, "Directory Club", "Austin", "TX", ct);
        await RefreshClubMembershipCookieAsync(admin, ct);
        await SeedRosterAsync(club.ClubId, ct);
        using var member = fixture.CreateNovaHttpClient();
        var memberEmail = UniqueEmail("directory-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(member, memberEmail, Password, ct);
        await UpdateUserAsync(memberEmail, "Taylor", "Member", club.ClubId, ct);
        await RefreshClubMembershipCookieAsync(member, ct);
        using var response = await member.GetAsync(new Uri(GetPlayerRosterEndpoints.GetSummaryUrl(club.ClubId), UriKind.Relative), ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = await response.Content.ReadFromJsonAsync<PlayerDirectorySummary>(ct);
        summary.ShouldNotBeNull();
        summary.ActiveCount.ShouldBe(1);
        summary.ArchivedCount.ShouldBe(1);
        summary.GraduationYears.ShouldBe([2031, 2032]);
        using var denied = await member.GetAsync(new Uri(GetPlayerRosterEndpoints.GetSummaryUrl(club.ClubId + 1000000), UriKind.Relative), ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync(ct)).ShouldContain("traceId");
    }

    [Fact]
    public async Task SummaryDeniesAnonymousRequestsAsync()
    {
        using var client = fixture.CreateNovaHttpClient();
        using var response = await client.GetAsync(new Uri(GetPlayerRosterEndpoints.GetSummaryUrl(1), UriKind.Relative), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
