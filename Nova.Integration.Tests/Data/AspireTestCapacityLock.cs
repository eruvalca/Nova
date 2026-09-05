using System.Globalization;

namespace Nova.Integration.Tests.Data;

/// <summary>Serializes Aspire-backed suites sharing this machine's container runtime.</summary>
internal static class AspireTestCapacityLock
{
    /// <summary>Bounds waiting for another suite without consuming the AppHost startup budget.</summary>
    private static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromMinutes(15);

    /// <summary>Waits for exclusive ownership of the machine lock until the caller disposes the returned handle.</summary>
    /// <param name="cancellationToken">Cancels waiting when the test run stops.</param>
    /// <returns>The operating-system-owned lock handle, released on disposal or process exit.</returns>
    public static Task<FileStream> AcquireAsync(CancellationToken cancellationToken)
    {
        var directory = Environment.GetEnvironmentVariable("NOVA_TEST_LOCK_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Nova", "verification")
                : "/tmp/nova-verification";
        }
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new InvalidOperationException("NOVA_TEST_LOCK_DIRECTORY must be an absolute, machine-shared directory.");
        }
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "aspire-tests.lock");
        var waitTimeout = DefaultWaitTimeout;
        var configuredTimeout = Environment.GetEnvironmentVariable("NOVA_TEST_LOCK_TIMEOUT_SECONDS");
        if (configuredTimeout is not null)
        {
            if (!int.TryParse(configuredTimeout, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || seconds is < 1 or > 3600)
            {
                throw new InvalidOperationException("NOVA_TEST_LOCK_TIMEOUT_SECONDS must be between 1 and 3600.");
            }
            waitTimeout = TimeSpan.FromSeconds(seconds);
        }

        return AcquireAsync(path, waitTimeout, cancellationToken);
    }

    /// <summary>Uses an explicit lock location and wait budget without changing process-wide configuration.</summary>
    internal static async Task<FileStream> AcquireAsync(string path, TimeSpan waitTimeout, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(waitTimeout);
        var announcedWait = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"Another Aspire test suite retained {path} for longer than {waitTimeout}.");
            }
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 11 or 32 or 33)
            {
                if (!announcedWait)
                {
                    Console.WriteLine($"Waiting for another Aspire test suite to release {path}.");
                    announcedWait = true;
                }
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"Another Aspire test suite retained {path} for longer than {waitTimeout}.", exception);
                }
            }
        }
    }
}
