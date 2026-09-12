using System.Text;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>Decodes the Closed-campaign roster export for integration assertions.</summary>
internal static class ClosedRosterCsv
{
    /// <summary>Returns the CRLF records after asserting the mandatory UTF-8 preamble.</summary>
    internal static string[] Lines(byte[] content)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        content.AsSpan(0, preamble.Length).SequenceEqual(preamble).ShouldBeTrue();
        return Encoding.UTF8.GetString(content, preamble.Length, content.Length - preamble.Length)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Returns the parsed cells of every record after the header row.</summary>
    internal static string[][] Rows(byte[] content)
        => [.. Lines(content).Skip(1).Select(line => line.Split(','))];
}
