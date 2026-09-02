using System.Net.Http.Json;
using System.Text.Json;
using Nova.Integration.Tests.Data;
using Nova.Shared.Features.Attention;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Verifies the club attention endpoint over HTTP: problem-details body shape for an unauthenticated
/// and member-level forbidden request.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class ClubAttentionHttpTests(NovaAppHostFixture fixture)
{
    /// <summary>Provides the password used by registered integration-test users.</summary>
    private const string Password = "Test#Passw0rd!";

    /// <summary>Verifies a non-admin club member receives a JSON ProblemDetails 403 from the attention endpoint.</summary>
    [Fact]
    public async Task GetAttention_MemberForbidden_ReturnsProblemDetailsBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var adminClient = fixture.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("attention-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, adminEmail, clubId: null, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        using var memberClient = fixture.CreateNovaHttpClient();
        var memberEmail = SeedingHelpers.UniqueEmail("attention-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture, memberEmail, club.ClubId, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(memberClient, cancellationToken);

        using var response = await memberClient.GetAsync(AttentionEndpoints.GetClubAttention, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        document.ShouldNotBeNull();
        document.RootElement.TryGetProperty("status", out var status).ShouldBeTrue();
        status.GetInt32().ShouldBe((int)HttpStatusCode.Forbidden);
        document.RootElement.TryGetProperty("traceId", out var traceId).ShouldBeTrue();
        traceId.GetString().ShouldNotBeNullOrWhiteSpace();
    }
}
