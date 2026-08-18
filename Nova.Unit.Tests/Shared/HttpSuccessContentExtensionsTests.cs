using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Shared;

/// <summary>
/// Verifies required-success-body handling shared by WebAssembly HTTP clients.
/// </summary>
public sealed class HttpSuccessContentExtensionsTests
{
    /// <summary>
    /// Verifies a JSON null token is rejected for non-nullable value types.
    /// </summary>
    [Fact]
    public async Task ReadRequiredJsonAsync_ReturnsServerError_WhenValueTypePayloadIsJsonNull()
    {
        using var content = JsonContent.Create<object?>(null);

        var result = await content.ReadRequiredJsonAsync<int>(
            "Invalid response.",
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a valid default value remains distinct from a JSON null token.
    /// </summary>
    [Fact]
    public async Task ReadRequiredJsonAsync_ReturnsDefaultValue_WhenValueTypePayloadIsValid()
    {
        using var content = new StringContent("0", Encoding.UTF8, "application/json");

        var result = await content.ReadRequiredJsonAsync<int>(
            "Invalid response.",
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
    }

    /// <summary>
    /// Verifies a required-property record round-trips through the strict options without throwing.
    /// </summary>
    [Fact]
    public async Task ReadRequiredJsonAsync_RoundTrips_RequiredPropertyRecord()
    {
        var payload = new CampaignCreationSetupResult
        {
            Seasons =
            [
                new CampaignSeasonChoice { SeasonId = 1, Name = "2025", StartDate = new DateOnly(2025, 1, 1), EndDate = null }
            ],
            TotalSeasonCount = 1,
            ActivePlayerCount = 12,
            ActiveTeamCount = 3
        };
        using var content = SerializeAsJson(payload);

        var result = await content.ReadRequiredJsonAsync<CampaignCreationSetupResult>(
            "Invalid response.",
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalSeasonCount.ShouldBe(1);
        result.Value.ActivePlayerCount.ShouldBe(12);
        result.Value.ActiveTeamCount.ShouldBe(3);
        result.Value.Seasons.Count.ShouldBe(1);
        result.Value.Seasons[0].Name.ShouldBe("2025");
    }

    /// <summary>
    /// Verifies a positional record with a nullable nested member round-trips through the strict options.
    /// </summary>
    [Fact]
    public async Task ReadRequiredJsonAsync_RoundTrips_PositionalRecordWithNullableNestedMember()
    {
        var payload = new CampaignPlacementRosterItem(
            101,
            202,
            "Zoe Adams",
            "Zoe",
            "Adams",
            2028,
            PlacementOutcome.Assigned,
            new CampaignParticipantTeamSummaryDto(21, "Blue"),
            new Guid("6f8b3e1a-0000-0000-0000-000000000001"));
        using var content = SerializeAsJson(payload);

        var result = await content.ReadRequiredJsonAsync<CampaignPlacementRosterItem>(
            "Invalid response.",
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(payload);
    }

    /// <summary>
    /// Verifies a type that mixes a parameterized constructor with a <c>required</c> member round-trips
    /// through the strict options without throwing, confirming the runtime handles the shape the review
    /// flagged as a potential <see cref="NotSupportedException"/> source. The broadened catch in the
    /// extension remains as defense-in-depth for any future serializer exception type.
    /// </summary>
    [Fact]
    public async Task ReadRequiredJsonAsync_RoundTrips_TypeWithParameterizedConstructorAndRequiredMembers()
    {
        using var content = SerializeAsJson(new ParameterizedConstructorWithRequiredMember(7) { Name = "Seven" });

        var result = await content.ReadRequiredJsonAsync<ParameterizedConstructorWithRequiredMember>(
            "Invalid response.",
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(7);
        result.Value.Name.ShouldBe("Seven");
    }

    private static StringContent SerializeAsJson<T>(T value)
        => new(JsonSerializer.Serialize(value, JsonSerializerOptions.Web), Encoding.UTF8, "application/json");

    /// <summary>
    /// A shape that mixes a parameterized constructor with a <c>required</c> member, which some
    /// serializer versions reject; used to confirm the current runtime round-trips it without throwing.
    /// </summary>
    private sealed class ParameterizedConstructorWithRequiredMember(int id)
    {
        public int Id { get; } = id;

        public required string Name { get; init; }
    }
}
