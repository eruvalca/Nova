using System.Diagnostics;
using Shouldly;

namespace Nova.Integration.Tests.Data;

// This class deliberately has no AppHost collection: it exercises a private lock path without starting containers.
public sealed class AspireTestCapacityLockTests
{
    [Fact]
    public async Task AcquireAsync_TimesOutWhileAnotherProcessOwnsLock_AndAcquiresAfterExit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("nova-capacity-lock-").FullName;
        var lockPath = Path.Combine(directory, "capacity.lock");
        var scriptPath = Path.Combine(directory, "hold-lock.ps1");
        const string script = """
            param([string]$LockPath)
            $ErrorActionPreference = 'Stop'
            $handle = [IO.File]::Open($LockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            try {
                [Console]::Out.WriteLine('LOCKED')
                [Console]::Out.Flush()
                $null = [Console]::In.ReadLine()
            }
            finally {
                $handle.Dispose()
            }
            """;
        using var child = new Process
        {
            StartInfo = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", scriptPath, "-LockPath", lockPath })
        {
            child.StartInfo.ArgumentList.Add(argument);
        }

        var started = false;
        try
        {
            await File.WriteAllTextAsync(scriptPath, script, cancellationToken);
            started = child.Start();
            started.ShouldBeTrue();
            var errorOutput = child.StandardError.ReadToEndAsync(cancellationToken);
            var ready = await child.StandardOutput.ReadLineAsync(cancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            ready.ShouldBe("LOCKED");

            // The outer deadline fails independently if the helper stops honoring its own short wait budget.
            await Should.ThrowAsync<TimeoutException>(() => AspireTestCapacityLock.AcquireAsync(
                lockPath, TimeSpan.FromMilliseconds(500), cancellationToken))
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            await child.StandardInput.WriteLineAsync("release".AsMemory(), cancellationToken);
            await child.StandardInput.FlushAsync(cancellationToken);
            await child.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            child.ExitCode.ShouldBe(0, await errorOutput);

            await using var acquired = await AspireTestCapacityLock.AcquireAsync(
                lockPath, TimeSpan.FromSeconds(1), cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            acquired.CanWrite.ShouldBeTrue();
        }
        finally
        {
            // Kill only this test's process if startup, assertions, cancellation, or orderly release failed.
            if (started && !child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await child.WaitForExitAsync(cleanupTimeout.Token);
            }
            File.Delete(scriptPath);
            File.Delete(lockPath);
            Directory.Delete(directory);
        }
    }
}
