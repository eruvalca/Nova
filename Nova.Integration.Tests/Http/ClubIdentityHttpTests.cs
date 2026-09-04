using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Shared.Features.Clubs;
using Shouldly;

namespace Nova.Integration.Tests.Http;

[Collection(NovaAppHostCollection.Name)]
public sealed class ClubIdentityHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Fact]
    public async Task GetCurrent_ReturnsUnauthorized_ForAnonymous()
    {
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.GetAsync(ClubEndpoints.GetCurrent, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCurrent_ReturnsForbidden_ForAuthenticatedUserWithoutMembership()
    {
        using var client = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client, SeedingHelpers.UniqueEmail("club-identity-clubless"), Password, TestContext.Current.CancellationToken);

        using var response = await client.GetAsync(ClubEndpoints.GetCurrent, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetCurrent_ReturnsCurrentTenantIdentity_ForAdministrator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client, SeedingHelpers.UniqueEmail("club-identity-admin"), Password, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(client, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);

        using var response = await client.GetAsync(ClubEndpoints.GetCurrent, cancellationToken);
        var identity = await response.Content.ReadFromJsonAsync<ClubIdentityResult>(cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        identity.ShouldNotBeNull();
        identity.ClubId.ShouldBe(club.ClubId);
        identity.Name.ShouldBe(club.Name);
        identity.City.ShouldBe(club.City);
        identity.State.ShouldBe(club.State);
        identity.HasCrest.ShouldBeTrue();
    }

    [Fact]
    public async Task GetCurrent_ReturnsCurrentTenantIdentity_ForMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var administrator = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            administrator, SeedingHelpers.UniqueEmail("club-identity-owner"), Password, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(administrator, cancellationToken);

        using var member = fixture.CreateNovaHttpClient();
        var memberEmail = SeedingHelpers.UniqueEmail("club-identity-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            member, memberEmail, Password, cancellationToken);
        await using (var context = fixture.CreateAdminContext())
        {
            var normalizedEmail = memberEmail.ToUpperInvariant();
            var user = await context.Users.SingleAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);
            user.ClubId = club.ClubId;
            await context.SaveChangesAsync(cancellationToken);
        }
        await SeedingHelpers.RefreshClubMembershipCookieAsync(member, cancellationToken);

        using var response = await member.GetAsync(ClubEndpoints.GetCurrent, cancellationToken);
        var identity = await response.Content.ReadFromJsonAsync<ClubIdentityResult>(cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        identity.ShouldNotBeNull();
        identity.ClubId.ShouldBe(club.ClubId);
        identity.Name.ShouldBe(club.Name);
        identity.HasCrest.ShouldBeTrue();
    }
}
