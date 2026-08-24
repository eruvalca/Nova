using System.Net;
using System.Net.Http.Headers;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Client.Services.Clubs;

/// <summary>
/// WebAssembly implementation of <see cref="IClubCrestService"/> that calls the server's
/// club crest minimal API endpoints over HTTP (with trace propagation via the DI-registered
/// <see cref="HttpClient"/>).
/// </summary>
/// <param name="httpClient">The DI-registered HTTP client with the app base address and trace propagation.</param>
public sealed class HttpClubCrestService(HttpClient httpClient) : IClubCrestService
{
    /// <inheritdoc />
    public async Task<ServiceResult<Success>> ChangeClubCrestAsync(long clubId, ClubCrestUpload upload, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(upload.Content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);
        form.Add(fileContent, "crest", "crest");

        using var response = await httpClient.PostAsync(ClubCrestEndpoints.ChangeCrestUrl(clubId), form, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new Success();
        }

        var problem = await response.ToServiceProblemAsync(cancellationToken);
        return problem;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<Success>> RemoveClubCrestAsync(long clubId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync(ClubCrestEndpoints.RemoveCrestUrl(clubId), cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new Success();
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ServiceProblem.NotFound();
        }

        var problem = await response.ToServiceProblemAsync(cancellationToken);
        return problem;
    }
}
