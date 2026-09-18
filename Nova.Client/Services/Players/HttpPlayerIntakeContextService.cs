using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Client.Services.Players;

/// <summary>
/// WebAssembly client implementation of <see cref="IPlayerIntakeContextService"/> that calls the
/// club-scoped manual-intake context API.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
internal sealed class HttpPlayerIntakeContextService(HttpClient http) : IPlayerIntakeContextService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlayerIntakeContext>> GetPlayerIntakeContextAsync(
        GetPlayerIntakeContextInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        using var response = await http.GetAsync(
            new Uri(GetPlayerRosterEndpoints.GetIntakeContextUrl(input.ClubId), UriKind.Relative),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<PlayerIntakeContext>(
            "The server returned an invalid player intake context.",
            // A missing campaign is an explicit null pair; a partial or blank pair is never dispatchable.
            context => context is not null
                && (context.CampaignId is null
                    ? context.CampaignName is null
                    : context.CampaignId > 0 && !string.IsNullOrWhiteSpace(context.CampaignName)),
            cancellationToken);
    }
}
