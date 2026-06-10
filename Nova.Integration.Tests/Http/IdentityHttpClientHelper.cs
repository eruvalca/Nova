using System.Text.RegularExpressions;

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
}
