using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Nova.Shared.Features.Photos;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// Drives the real Identity registration flow over HTTP for integration tests: scrapes the
/// Blazor SSR register form (antiforgery token and hidden fields), posts the registration,
/// and leaves the Identity application cookie in the client's cookie container so subsequent
/// API requests are authenticated.
/// </summary>
internal static partial class IdentityHttpClientHelper
{
    /// <summary>
    /// Upload retry bounds are deliberately hard-coded (not environment-tunable): environment-tunable
    /// retry windows are scoped to the browser hydration retries in <c>Nova.Browser.Tests</c>. The
    /// upload is a safe retry target — the server upserts the profile-photo entity and cleans up its
    /// own partial blobs, and each attempt posts a fresh blob batch.
    /// </summary>
    private const int UploadRetryMaxAttempts = 5;

    private static readonly TimeSpan UploadRetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Registers a user, uploads a profile photo, and performs the profile-photo cookie-refresh
    /// hop so subsequent non-account requests are not redirected by the profile-photo gate.
    /// </summary>
    /// <param name="client">A cookie-enabled, non-redirect-following client from the fixture.</param>
    /// <param name="email">The new user's unique email address.</param>
    /// <param name="password">The new user's password.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when onboarding is fully complete.</returns>
    /// <exception cref="InvalidOperationException">Thrown when any onboarding step fails.</exception>
    public static async Task RegisterUserWithCompletedProfilePhotoAsync(
        HttpClient client,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        await RegisterUserAsync(client, email, password, cancellationToken);
        await UploadProfilePhotoWithRetryAsync(client, email, cancellationToken);

        using var complete = await client.GetAsync($"{PhotoEndpoints.Complete}?returnUrl=/", cancellationToken);
        if (complete.StatusCode is not (System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.Found))
        {
            var body = await complete.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Profile photo complete for '{email}' returned {(int)complete.StatusCode}. Body (truncated): {body[..Math.Min(body.Length, 2000)]}");
        }
    }

    /// <summary>
    /// Registers a new user through the real <c>/Account/Register</c> page. On success the
    /// client's cookie container holds the Identity application cookie and the server redirects
    /// to the profile photo page (a profile photo is required for new users).
    /// </summary>
    /// <param name="client">A cookie-enabled, non-redirect-following client from the fixture.</param>
    /// <param name="email">The new user's email (must be unique per test).</param>
    /// <param name="password">The new user's password.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes when the user is registered and signed in.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the registration flow does not behave as expected.</exception>
    public static async Task RegisterUserAsync(
        HttpClient client,
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var registerPage = await client.GetAsync("/Account/Register", cancellationToken);
        registerPage.EnsureSuccessStatusCode();
        var html = await registerPage.Content.ReadAsStringAsync(cancellationToken);

        var form = new Dictionary<string, string>
        {
            ["Input.FirstName"] = "Test",
            ["Input.LastName"] = "User",
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password
        };

        // Carry over every hidden input the SSR form rendered (antiforgery token and the
        // Blazor named-form handler field), so the post matches what a browser would send.
        foreach (Match hidden in HiddenInputRegex().Matches(html))
        {
            form.TryAdd(hidden.Groups["name"].Value, hidden.Groups["value"].Value);
        }

        if (!form.ContainsKey("__RequestVerificationToken"))
        {
            throw new InvalidOperationException("No antiforgery token was found on the register page.");
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync("/Account/Register", content, cancellationToken);

        // Successful registration signs the user in and redirects to the required photo step.
        if (response.StatusCode is not (System.Net.HttpStatusCode.Redirect or System.Net.HttpStatusCode.Found))
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Registration for '{email}' returned {(int)response.StatusCode} instead of a redirect. Body (truncated): {body[..Math.Min(body.Length, 2000)]}");
        }
    }

    /// <summary>
    /// Matches hidden form inputs rendered by the Blazor SSR register form, capturing their
    /// name and value attributes.
    /// </summary>
    /// <returns>The compiled regex.</returns>
    [GeneratedRegex("""<input[^>]*type="hidden"[^>]*name="(?<name>[^"]+)"[^>]*value="(?<value>[^"]*)"[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenInputRegex();

    /// <summary>
    /// Posts the profile-photo upload, retrying only transient failures (<see cref="HttpRequestException"/>
    /// and 5xx responses). A fresh <see cref="MultipartFormDataContent"/> is built per attempt because
    /// each attempt's content is disposed after its POST. Non-transient statuses fail fast with the
    /// existing truncated-body message; if every attempt fails transiently, the thrown message lists
    /// each attempt's status.
    /// </summary>
    /// <param name="client">A cookie-enabled, non-redirect-following client from the fixture.</param>
    /// <param name="email">The new user's email address, used in failure messages.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task that completes once the upload succeeds with 204 No Content.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a non-transient status is returned, or when every attempt fails transiently.
    /// </exception>
    private static async Task UploadProfilePhotoWithRetryAsync(
        HttpClient client,
        string email,
        CancellationToken cancellationToken)
    {
        var jpeg = CreateJpeg(width: 256, height: 256);
        var attemptStatuses = new List<string>();

        for (var attempt = 1; attempt <= UploadRetryMaxAttempts; attempt++)
        {
            using var content = CreateUploadContent(jpeg, "image/jpeg");

            HttpResponseMessage upload;
            try
            {
                upload = await client.PostAsync(PhotoEndpoints.Upload, content, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                // Transient transport failure; retry with a fresh multipart payload.
                attemptStatuses.Add($"transport error: {exception.Message}");
                if (attempt < UploadRetryMaxAttempts)
                {
                    await Task.Delay(UploadRetryDelay, cancellationToken);
                }

                continue;
            }

            using (upload)
            {
                if (upload.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return;
                }

                var body = await upload.Content.ReadAsStringAsync(cancellationToken);
                var truncated = body[..Math.Min(body.Length, 2000)];
                attemptStatuses.Add($"{(int)upload.StatusCode}: {truncated}");

                // Only 5xx responses are transient and worth retrying; anything else (4xx client
                // errors, 3xx redirects, unexpected 2xx) is a genuine onboarding failure and fails fast.
                if ((int)upload.StatusCode is < 500 or >= 600)
                {
                    throw new InvalidOperationException(
                        $"Profile photo upload for '{email}' returned {(int)upload.StatusCode}. Body (truncated): {truncated}");
                }

                if (attempt < UploadRetryMaxAttempts)
                {
                    await Task.Delay(UploadRetryDelay, cancellationToken);
                }
            }
        }

        throw new InvalidOperationException(
            $"Profile photo upload for '{email}' failed after {UploadRetryMaxAttempts} attempts. " +
            $"Attempt statuses: {string.Join(" | ", attemptStatuses)}");
    }

    /// <summary>
    /// Builds multipart content for the profile-photo upload endpoint.
    /// </summary>
    /// <param name="bytes">The uploaded image bytes.</param>
    /// <param name="contentType">The declared media type.</param>
    /// <returns>The multipart payload for the <c>file</c> form field.</returns>
    private static MultipartFormDataContent CreateUploadContent(byte[] bytes, string contentType)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { fileContent, "file", "photo.jpg" } };
    }

    /// <summary>
    /// Creates a valid in-memory JPEG used by profile-photo onboarding helpers.
    /// </summary>
    /// <param name="width">The JPEG width.</param>
    /// <param name="height">The JPEG height.</param>
    /// <returns>The JPEG bytes.</returns>
    private static byte[] CreateJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(80, 140, 200));
        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder());
        return stream.ToArray();
    }
}
