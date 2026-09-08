using System.Net;
using System.Net.Http.Headers;
using Nova.SharedKernel.Features.Photos;
using Nova.SharedKernel.Results;
using OneOf.Types;

namespace Nova.Client.Services.Photos;

/// <summary>
/// WebAssembly implementation of <see cref="IProfilePhotoService"/> that calls the server's
/// profile photo minimal API endpoints over HTTP (with trace propagation via the DI-registered
/// <see cref="HttpClient"/>).
/// </summary>
/// <param name="httpClient">The DI-registered HTTP client with the app base address and trace propagation.</param>
internal sealed class HttpProfilePhotoService(HttpClient httpClient) : IProfilePhotoService
{
    /// <inheritdoc />
    public async Task<ServiceResult<Success>> SaveProfilePhotoAsync(ProfilePhotoUpload upload, CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(upload.Content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);
        form.Add(fileContent, "file", upload.FileName);

        using var response = await httpClient.PostAsync(new Uri(PhotoEndpoints.Upload, UriKind.RelativeOrAbsolute), form, cancellationToken);
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
        using var response = await httpClient.GetAsync(new Uri(PhotoEndpoints.Status, UriKind.RelativeOrAbsolute), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ServiceProblem.NotFound();
        }

        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<ProfilePhotoInfo>(
            "The server returned an invalid profile photo response.",
            info => info.NovaUserId > 0
                && (info.ContentType is null || !string.IsNullOrWhiteSpace(info.ContentType)),
            cancellationToken);
    }
}
