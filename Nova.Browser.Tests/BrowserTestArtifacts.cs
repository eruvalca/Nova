using System.Security.Cryptography;
using System.Text;

namespace Nova.Browser.Tests;

/// <summary>Isolates browser diagnostics by run and xUnit test, including parallel screenshot helpers.</summary>
internal static class BrowserTestArtifacts
{
    public static string RunDirectory { get; } = CreateRunDirectory();

    public static string ForCurrentTest(string category)
    {
        var test = TestContext.Current.Test;
        var identity = test?.UniqueID ?? "fixture";
        var name = test?.TestDisplayName ?? "fixture";
        var safeName = new string(name.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').Take(80).ToArray());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12];
        var directory = Path.Combine(RunDirectory, $"{safeName}-{hash}", category);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateRunDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("NOVA_TEST_ARTIFACTS");
        string directory;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Path.IsPathFullyQualified(configured))
            {
                throw new InvalidOperationException("NOVA_TEST_ARTIFACTS must be an absolute directory.");
            }
            directory = Path.Combine(configured, "browser");
        }
        else
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "Nova.slnx")))
            {
                repository = repository.Parent;
            }
            if (repository is null)
            {
                throw new InvalidOperationException("Set NOVA_TEST_ARTIFACTS when running browser tests outside a Nova checkout.");
            }
            directory = Path.Combine(repository.FullName, "artifacts", "verification", $"browser-{DateTime.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}");
        }
        Directory.CreateDirectory(directory);
        return directory;
    }
}
