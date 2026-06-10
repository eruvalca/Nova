using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Nova.Shared.Photos;
using Nova.Shared.Results;
using OneOf.Types;

namespace Nova.Client.Services;

/// <summary>
/// WebAssembly implementation of <see cref="IProfilePhotoService"/> that calls the server's
/// profile photo minimal API endpoints over HTTP (with trace propagation via the DI-registered
/// <see cref="HttpClient"/>).
/// </summary>
/// <param name="httpClient">The DI-registered HTTP client with the app base address and trace propagation.</param>
public sealed class HttpProfilePhotoService(HttpClient httpClient) : IProfilePhotoService
{
    /// <inheritdoc />
    public async Task<ServiceResult<Success>> SaveProfilePhotoAsync(ProfilePhotoUpload upload, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(upload.Content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);
        form.Add(fileContent, "file", upload.FileName);

        using var response = await httpClient.PostAsync(PhotoEndpoints.Upload, form, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new Success();
        }

        var problem = await response.ToServiceProblemAsync(cancellationToken);
        return problem;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ProfilePhotoInfo>> GetCurrentUserPhotoAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(PhotoEndpoints.Status, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ServiceProblem.NotFound();
        }

        response.EnsureSuccessStatusCode();
        var info = await response.Content.ReadFromJsonAsync<ProfilePhotoInfo>(cancellationToken);
        return info is null ? ServiceProblem.NotFound() : info;
    }
}
