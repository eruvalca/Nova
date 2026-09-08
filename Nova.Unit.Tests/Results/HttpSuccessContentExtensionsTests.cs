using System.Net.Http.Json;
using System.Text;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Results;

/// <summary>
/// Verifies required-success-body handling shared by WebAssembly HTTP clients.
/// </summary>
public sealed class HttpSuccessContentExtensionsTests
{
    /// <summary>
    /// Verifies a JSON null token is rejected for non-nullable value types.
    /// </summary>
    [Fact]
    public async Task ReadRequiredJsonAsyncReturnsServerErrorWhenValueTypePayloadIsJsonNullAsync()
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
    public async Task ReadRequiredJsonAsyncReturnsDefaultValueWhenValueTypePayloadIsValidAsync()
    {
        using var content = new StringContent("0", Encoding.UTF8, "application/json");

        var result = await content.ReadRequiredJsonAsync<int>(
            "Invalid response.",
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(0);
    }
}
