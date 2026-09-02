using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Validation;
using OneOf;

namespace Nova.Features.Players;

/// <summary>Represents a structurally valid, bounded player CSV.</summary>
internal sealed record ParsedPlayerImport(IReadOnlyList<PlayerImportPreviewRow> Rows);

/// <summary>Represents a file-level CSV rejection.</summary>
internal sealed record PlayerImportFileFailure(string Message);

/// <summary>Strictly parses the authoritative player CSV format.</summary>
internal sealed class PlayerImportCsvParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly byte[] Utf8Preamble = [0xEF, 0xBB, 0xBF];

    /// <summary>Parses the supplied bytes without performing database work.</summary>
    public OneOf<ParsedPlayerImport, PlayerImportFileFailure> Parse(
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (HasUnsupportedUnicodePreamble(content) || content.Contains((byte)0))
        {
            return new PlayerImportFileFailure("The file must use UTF-8 encoding.");
        }

        var offset = content.AsSpan().StartsWith(Utf8Preamble) ? Utf8Preamble.Length : 0;
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            DetectDelimiter = false,
            HasHeaderRecord = true,
            IgnoreBlankLines = false,
            TrimOptions = TrimOptions.None,
            Mode = CsvMode.RFC4180,
            MaxFieldSize = PlayerImportConstraints.MaxFieldCharacters
        };

        try
        {
            using var stream = new MemoryStream(content, offset, content.Length - offset, writable: false);
            using var reader = new StreamReader(
                stream,
                StrictUtf8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: false);
            using var csv = new CsvReader(reader, configuration);

            if (!csv.Read())
            {
                return new PlayerImportFileFailure("The file must contain the required header row.");
            }

            csv.ReadHeader();
            if (!HeadersMatch(csv.HeaderRecord))
            {
                return new PlayerImportFileFailure(
                    $"The header row must be exactly: {string.Join(", ", PlayerImportConstraints.Headers)}.");
            }

            var rows = new List<PlayerImportPreviewRow>();
            var sourceRowNumber = 1;
            while (csv.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourceRowNumber++;

                if (rows.Count == PlayerImportConstraints.MaxDataRows)
                {
                    return new PlayerImportFileFailure(
                        $"The file may contain no more than {PlayerImportConstraints.MaxDataRows} data rows.");
                }

                var record = (csv.Parser.Record ?? [])
                    .Select(value => value ?? string.Empty)
                    .ToArray();
                if (record.Length != PlayerImportConstraints.Headers.Count)
                {
                    if (record.Length == 1 && record[0].Length == 0)
                    {
                        record = Enumerable
                            .Repeat(string.Empty, PlayerImportConstraints.Headers.Count)
                            .ToArray();
                    }
                    else
                    {
                        return new PlayerImportFileFailure(
                            $"Source row {sourceRowNumber} must contain exactly {PlayerImportConstraints.Headers.Count} columns.");
                    }
                }

                rows.Add(ParseRow(sourceRowNumber, record));
            }

            if (rows.Count == 0)
            {
                return new PlayerImportFileFailure("The file must contain at least one data row.");
            }

            return new ParsedPlayerImport(rows.AsReadOnly());
        }
        catch (DecoderFallbackException)
        {
            return new PlayerImportFileFailure("The file must use valid UTF-8 encoding.");
        }
        catch (CsvHelperException exception)
        {
            var row = exception.Context?.Parser?.Row;
            var rowDetail = row is > 1 ? $" near source row {row}" : string.Empty;
            return new PlayerImportFileFailure($"The file contains malformed CSV data{rowDetail}.");
        }
    }

    private static PlayerImportPreviewRow ParseRow(int sourceRowNumber, string[] record)
    {
        var values = new PlayerImportRowValues(
            record[0],
            record[1],
            record[2],
            record[3],
            record[4],
            record[5]);
        var errors = new List<PlayerImportFieldError>();

        AddFormulaError(values.FirstName, PlayerImportField.FirstName, errors);
        AddFormulaError(values.LastName, PlayerImportField.LastName, errors);
        AddFormulaError(values.DateOfBirth, PlayerImportField.DateOfBirth, errors);
        AddFormulaError(values.Gender, PlayerImportField.Gender, errors);
        AddFormulaError(values.JerseyNumber, PlayerImportField.JerseyNumber, errors);
        AddFormulaError(values.GraduationYear, PlayerImportField.GraduationYear, errors);

        var dateOfBirth = default(DateOnly);
        if (!HasError(errors, PlayerImportField.DateOfBirth)
            && !DateOnly.TryParseExact(
                values.DateOfBirth,
                PlayerImportConstraints.DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out dateOfBirth))
        {
            errors.Add(new(
                PlayerImportField.DateOfBirth,
                $"Date of birth must use the {PlayerImportConstraints.DateFormat} format."));
        }

        Gender? gender = null;
        if (!string.IsNullOrEmpty(values.Gender)
            && !HasError(errors, PlayerImportField.Gender))
        {
            if (Enum.TryParse<Gender>(values.Gender, ignoreCase: true, out var parsedGender)
                && Enum.IsDefined(parsedGender)
                && string.Equals(
                    Enum.GetName(parsedGender),
                    values.Gender,
                    StringComparison.OrdinalIgnoreCase))
            {
                gender = parsedGender;
            }
            else
            {
                errors.Add(new(
                    PlayerImportField.Gender,
                    "Gender must be empty, Male, Female, or Other."));
            }
        }

        int? jerseyNumber = null;
        if (!string.IsNullOrEmpty(values.JerseyNumber)
            && !HasError(errors, PlayerImportField.JerseyNumber))
        {
            if (TryParseUnsignedInt(values.JerseyNumber, out var parsedJerseyNumber))
            {
                jerseyNumber = parsedJerseyNumber;
            }
            else
            {
                errors.Add(new(PlayerImportField.JerseyNumber, "Jersey number must contain digits only."));
            }
        }

        var graduationYear = default(int);
        if (!HasError(errors, PlayerImportField.GraduationYear)
            && !TryParseUnsignedInt(values.GraduationYear, out graduationYear))
        {
            errors.Add(new(PlayerImportField.GraduationYear, "Graduation year must contain digits only."));
        }

        var candidate = new CreatePlayerInput
        {
            FirstName = values.FirstName,
            LastName = values.LastName,
            DateOfBirth = dateOfBirth,
            Gender = gender,
            JerseyNumber = jerseyNumber,
            GraduationYear = graduationYear
        };

        foreach (var (memberName, messages) in InputValidator.Validate(candidate))
        {
            if (!TryMapField(memberName, out var field) || HasError(errors, field))
            {
                continue;
            }

            errors.AddRange(messages.Select(message => new PlayerImportFieldError(field, message)));
        }

        return new PlayerImportPreviewRow(
            sourceRowNumber,
            values,
            errors.Count == 0 ? candidate : null,
            errors.Count == 0 ? PlayerImportRowStatus.Ready : PlayerImportRowStatus.Invalid,
            errors.AsReadOnly(),
            Duplicate: null);
    }

    private static void AddFormulaError(
        string value,
        PlayerImportField field,
        ICollection<PlayerImportFieldError> errors)
    {
        if (!IsFormulaLike(value))
        {
            return;
        }

        errors.Add(new(field, "The value must not begin with a spreadsheet formula character."));
    }

    private static bool IsFormulaLike(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value[0] is '\t' or '\r' or '\n')
        {
            return true;
        }

        var firstMeaningfulCharacter = value.FirstOrDefault(character => character != ' ');
        return firstMeaningfulCharacter is '=' or '+' or '-' or '@';
    }

    private static bool TryParseUnsignedInt(string value, out int result)
    {
        result = default;
        return value.Length > 0
            && value.All(char.IsAsciiDigit)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    private static bool HeadersMatch(string[]? headers) =>
        headers is not null
        && headers.Length == PlayerImportConstraints.Headers.Count
        && headers.SequenceEqual(PlayerImportConstraints.Headers, StringComparer.Ordinal);

    private static bool HasUnsupportedUnicodePreamble(ReadOnlySpan<byte> content) =>
        content.Length >= 2
        && ((content[0] == 0xFF && content[1] == 0xFE)
            || (content[0] == 0xFE && content[1] == 0xFF))
        || content.Length >= 4
        && ((content[0] == 0x00 && content[1] == 0x00 && content[2] == 0xFE && content[3] == 0xFF)
            || (content[0] == 0xFF && content[1] == 0xFE && content[2] == 0x00 && content[3] == 0x00));

    private static bool HasError(IEnumerable<PlayerImportFieldError> errors, PlayerImportField field) =>
        errors.Any(error => error.Field == field);

    private static bool TryMapField(string memberName, out PlayerImportField field)
    {
        field = memberName switch
        {
            nameof(CreatePlayerInput.FirstName) => PlayerImportField.FirstName,
            nameof(CreatePlayerInput.LastName) => PlayerImportField.LastName,
            nameof(CreatePlayerInput.DateOfBirth) => PlayerImportField.DateOfBirth,
            nameof(CreatePlayerInput.Gender) => PlayerImportField.Gender,
            nameof(CreatePlayerInput.JerseyNumber) => PlayerImportField.JerseyNumber,
            nameof(CreatePlayerInput.GraduationYear) => PlayerImportField.GraduationYear,
            _ => default
        };

        return memberName is nameof(CreatePlayerInput.FirstName)
            or nameof(CreatePlayerInput.LastName)
            or nameof(CreatePlayerInput.DateOfBirth)
            or nameof(CreatePlayerInput.Gender)
            or nameof(CreatePlayerInput.JerseyNumber)
            or nameof(CreatePlayerInput.GraduationYear);
    }
}
