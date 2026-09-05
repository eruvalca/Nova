namespace Nova.Shared.Features.Players;

/// <summary>
/// Defines the stable limits and wire values for player CSV import.
/// </summary>
public static class PlayerImportConstraints
{
    /// <summary>The bounded protected confirmation size, including all preview classifications.</summary>
    public const int MaxConfirmationTokenCharacters = 32 * 1024;

    /// <summary>The number of hours a completed import can be recovered.</summary>
    public const int RecoveryLifetimeHours = 24;

    /// <summary>The multipart preview identity field.</summary>
    public const string OperationIdFormFieldName = "operationId";

    /// <summary>The multipart protected confirmation field.</summary>
    public const string ConfirmationTokenFormFieldName = "confirmationToken";

    /// <summary>The maximum number of data rows accepted in one upload.</summary>
    public const int MaxDataRows = 1_000;

    /// <summary>The maximum number of bytes accepted for one CSV file.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    /// <summary>The bounded multipart overhead allowed above the file bytes.</summary>
    public const int MultipartOverheadBytes = 64 * 1024;

    /// <summary>The maximum accepted HTTP request size.</summary>
    public const int MaxRequestBytes = MaxFileBytes + MultipartOverheadBytes;

    /// <summary>The maximum number of characters allowed in one parsed cell.</summary>
    public const int MaxFieldCharacters = 1_024;

    /// <summary>The one accepted date format.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>The safe filename supplied for the generated template.</summary>
    public const string TemplateFileName = "nova-player-import-template.csv";

    /// <summary>The CSV response media type.</summary>
    public const string CsvContentType = "text/csv; charset=utf-8";

    /// <summary>The multipart field name used by the preview endpoint.</summary>
    public const string FileFormFieldName = "file";

    /// <summary>The lifetime, in minutes, of a preview confirmation token.</summary>
    public const int PreviewLifetimeMinutes = 60;

    /// <summary>The exact ordered header row accepted by the parser.</summary>
    public static IReadOnlyList<string> Headers { get; } =
    [
        "First name",
        "Last name",
        "Date of birth",
        "Gender",
        "Jersey number",
        "Graduation year"
    ];

    /// <summary>The browser media types accepted for a CSV upload.</summary>
    public static IReadOnlySet<string> AllowedContentTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "text/csv",
        "application/csv",
        "application/vnd.ms-excel",
        "application/octet-stream"
    };
}
