using System.Text;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>Minimal RFC 4180 reader for asserting on generated Closed-roster export bytes.</summary>
internal static class ClosedRosterCsv
{
    /// <summary>Confirms the export is UTF-8 BOM prefixed.</summary>
    internal static bool HasPreamble(byte[] content)
        => content.Length >= Encoding.UTF8.GetPreamble().Length
            && content.AsSpan(0, Encoding.UTF8.GetPreamble().Length).SequenceEqual(Encoding.UTF8.GetPreamble());

    /// <summary>Decodes the file after the BOM, asserting the mandatory UTF-8 preamble.</summary>
    internal static string Text(byte[] content)
    {
        HasPreamble(content).ShouldBeTrue();
        var preamble = Encoding.UTF8.GetPreamble().Length;
        return Encoding.UTF8.GetString(content, preamble, content.Length - preamble);
    }

    /// <summary>Returns the parsed cells of one record.</summary>
    internal static string[] Cells(byte[] content, int recordIndex) => Parse(content)[recordIndex];

    /// <summary>Parses every record, treating quoted delimiters and newlines as cell content.</summary>
    internal static List<string[]> Parse(byte[] content)
    {
        var text = Text(content);
        var records = new List<string[]>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var index = 0;

        while (index < text.Length)
        {
            var character = text[index];
            if (quoted && character == '"' && index + 1 < text.Length && text[index + 1] == '"')
            {
                field.Append('"');
                index += 2;
                continue;
            }
            if (quoted && character == '"')
            {
                quoted = false;
            }
            else if (quoted)
            {
                field.Append(character);
            }
            else if (character == '"')
            {
                quoted = true;
            }
            else if (character == ',')
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                record.Add(field.ToString());
                field.Clear();
                records.Add([.. record]);
                record.Clear();
                index += 2;
                continue;
            }
            else
            {
                field.Append(character);
            }
            index++;
        }

        return records;
    }
}
