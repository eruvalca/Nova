using System.Security.Cryptography;
using System.Text;
using Nova.Integration.Tests.Http;

namespace Nova.Browser.Tests;

/// <summary>
/// Parallel-safe browser setup helpers for account-management scenarios. Each helper creates
/// unique Identity state and uses the real static-SSR authenticator enrollment flow, rather than
/// toggling an Identity flag directly in the database.
/// </summary>
internal static class AccountManageBrowserHelpers
{
    /// <summary>
    /// Registers a user and completes the required profile-photo gate through the real HTTP flow.
    /// </summary>
    /// <param name="fixture">The shared browser fixture.</param>
    /// <param name="prefix">The unique e-mail prefix.</param>
    /// <param name="password">The password used for sign-in.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The registered user's unique e-mail address.</returns>
    public static async Task<string> SeedPhotoCompleteUserAsync(
        BrowserSuiteFixture fixture,
        string prefix,
        string password,
        CancellationToken cancellationToken)
    {
        using var client = fixture.AppHost.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail(prefix);
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client,
            email,
            password,
            cancellationToken);
        return email;
    }

    /// <summary>
    /// Registers a user without completing the required photo gate, leaving the account ready for
    /// the browser onboarding upload scenario.
    /// </summary>
    /// <param name="fixture">The shared browser fixture.</param>
    /// <param name="prefix">The unique e-mail prefix.</param>
    /// <param name="password">The password used for sign-in.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The registered user's unique e-mail address.</returns>
    public static async Task<string> SeedPhotoLessUserAsync(
        BrowserSuiteFixture fixture,
        string prefix,
        string password,
        CancellationToken cancellationToken)
    {
        using var client = fixture.AppHost.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail(prefix);
        await IdentityHttpClientHelper.RegisterUserAsync(client, email, password, cancellationToken);
        return email;
    }

    /// <summary>
    /// Enrolls the signed-in browser in authenticator-based 2FA through the real setup page and
    /// waits for the one-time recovery-code display. The resulting state includes an authenticator
    /// key, enabled 2FA, and generated recovery codes.
    /// </summary>
    /// <param name="page">The signed-in browser page.</param>
    /// <param name="baseUri">The running Nova base URI.</param>
    /// <returns>A task that completes after the recovery codes are rendered.</returns>
    public static async Task EnableTwoFactorAsync(IPage page, Uri baseUri)
    {
        await page.GotoAsync(new Uri(baseUri, "/Account/Manage/EnableAuthenticator").ToString());
        var sharedKey = await page.Locator("kbd").InnerTextAsync();
        var verificationCode = GenerateTotp(sharedKey);

        await page.GetByLabel("Verification code").FillAsync(verificationCode);
        await page.GetByRole(AriaRole.Button, new() { Name = "Verify", Exact = true }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Recovery codes", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    /// <summary>
    /// Computes the current six-digit RFC 6238 TOTP for the displayed Base32 authenticator key.
    /// </summary>
    /// <param name="formattedKey">The key rendered by the authenticator page.</param>
    /// <returns>The six-digit verification code.</returns>
    private static string GenerateTotp(string formattedKey)
    {
        var key = DecodeBase32(formattedKey);
        var counter = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        Span<byte> counterBytes = stackalloc byte[sizeof(long)];
        for (var index = counterBytes.Length - 1; index >= 0; index--)
        {
            counterBytes[index] = (byte)(counter & 0xff);
            counter >>= 8;
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// Decodes an RFC 4648 Base32 string without requiring padding.
    /// </summary>
    /// <param name="formattedKey">The formatted key, which may contain spaces.</param>
    /// <returns>The decoded authenticator secret.</returns>
    private static byte[] DecodeBase32(string formattedKey)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var normalized = formattedKey.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var bytes = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitCount = 0;

        foreach (var character in normalized)
        {
            var value = alphabet.IndexOf(character);
            if (value < 0)
            {
                throw new FormatException($"Authenticator key contains an invalid Base32 character: {character}.");
            }

            buffer = (buffer << 5) | value;
            bitCount += 5;
            if (bitCount < 8)
            {
                continue;
            }

            bitCount -= 8;
            bytes.Add((byte)(buffer >> bitCount));
            buffer &= (1 << bitCount) - 1;
        }

        return bytes.ToArray();
    }
}
