namespace Nova.Browser.Tests;

/// <summary>
/// Lazily-initialized, environment-tunable policy for the browser suite's SSR-hydration retry
/// windows. The two <c>NOVA_BROWSER_RETRY_*</c> variables are read exactly once (on first access);
/// missing, invalid, or non-positive values fall back to the documented defaults.
/// </summary>
/// <remarks>
/// <para>
/// Hydration retries tolerate the window during which Blazor SSR-prerendered controls ignore clicks
/// and key presses before the interactive circuit attaches. The defaults (<see cref="MaxAttempts"/>
/// = 60, <see cref="Delay"/> = 250&nbsp;ms) are deliberately more generous than the previous
/// hard-coded 40 attempts so the suite stays deterministic under its 4-way parallel Chromium load.
/// </para>
/// <para>
/// The knobs are scoped to browser hydration retries only. The Azurite/upload seeding retries in
/// <c>Nova.Integration.Tests</c> remain documented hard-coded constants, per issue #130.
/// </para>
/// </remarks>
internal static class BrowserRetryPolicy
{
    private const int DefaultMaxAttempts = 60;
    private const int DefaultDelayMilliseconds = 250;

    private static readonly Lazy<int> MaxAttemptsValue = new(ReadMaxAttempts);
    private static readonly Lazy<int> DelayValue = new(ReadDelay);

    /// <summary>Gets the maximum number of hydration-retry attempts before a helper fails.</summary>
    public static int MaxAttempts => MaxAttemptsValue.Value;

    /// <summary>Gets the delay in milliseconds between hydration-retry attempts.</summary>
    public static int Delay => DelayValue.Value;

    /// <summary>Reads and validates <c>NOVA_BROWSER_RETRY_MAX_ATTEMPTS</c>.</summary>
    private static int ReadMaxAttempts() =>
        ReadPositiveInt("NOVA_BROWSER_RETRY_MAX_ATTEMPTS", DefaultMaxAttempts);

    /// <summary>Reads and validates <c>NOVA_BROWSER_RETRY_DELAY_MS</c>.</summary>
    private static int ReadDelay() =>
        ReadPositiveInt("NOVA_BROWSER_RETRY_DELAY_MS", DefaultDelayMilliseconds);

    /// <summary>
    /// Parses the named environment variable as a positive integer, returning <paramref name="fallback"/>
    /// when the value is unset, non-numeric, or non-positive.
    /// </summary>
    /// <param name="variable">The environment variable name to read.</param>
    /// <param name="fallback">The default value used when the variable is invalid or absent.</param>
    /// <returns>The parsed positive value, or the fallback.</returns>
    private static int ReadPositiveInt(string variable, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(variable);
        return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
    }
}
