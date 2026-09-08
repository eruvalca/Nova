using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using OneOf.Types;

namespace Nova.Client.Services.Tags;

/// <summary>
/// WebAssembly client implementation of <see cref="ITagDefinitionLifecycleService"/> that calls
/// the tag-definition lifecycle endpoints over HTTP.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
internal sealed class HttpTagDefinitionLifecycleService(HttpClient http) : ITagDefinitionLifecycleService
{
    /// <inheritdoc />
    public Task<ServiceResult<Success>> ArchiveAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default)
        => SendMutationAsync(TagEndpoints.ArchiveUrl(tagDefinitionId), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<Success>> RestoreAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default)
        => SendMutationAsync(TagEndpoints.RestoreUrl(tagDefinitionId), cancellationToken);

    private async Task<ServiceResult<Success>> SendMutationAsync(
        string requestUri,
        CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(new Uri(requestUri, UriKind.RelativeOrAbsolute), content: null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return new Success();
    }
}
