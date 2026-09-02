using Nova.Shared.Enums;

namespace Nova.Shared.Features.Players;

/// <summary>Classifies one row in a player import preview.</summary>
public enum PlayerImportRowStatus
{
    /// <summary>The row passed validation and duplicate checks.</summary>
    Ready,

    /// <summary>The row contains one or more field validation errors.</summary>
    Invalid,

    /// <summary>The row matches an existing player or an earlier uploaded row.</summary>
    Duplicate
}

/// <summary>Classifies the record that caused an uploaded row to be blocked as a duplicate.</summary>
public enum PlayerImportDuplicateKind
{
    /// <summary>The row matches an active player in the current club.</summary>
    ExistingActivePlayer,

    /// <summary>The row matches an archived player in the current club.</summary>
    ExistingArchivedPlayer,

    /// <summary>The row matches an earlier row in the same upload.</summary>
    EarlierUploadRow
}

/// <summary>Identifies a stable field in the player CSV contract.</summary>
public enum PlayerImportField
{
    /// <summary>The first-name cell.</summary>
    FirstName,

    /// <summary>The last-name cell.</summary>
    LastName,

    /// <summary>The date-of-birth cell.</summary>
    DateOfBirth,

    /// <summary>The gender cell.</summary>
    Gender,

    /// <summary>The jersey-number cell.</summary>
    JerseyNumber,

    /// <summary>The graduation-year cell.</summary>
    GraduationYear
}

/// <summary>Preserves the six original cell values from one source record.</summary>
/// <param name="FirstName">The original first-name cell text.</param>
/// <param name="LastName">The original last-name cell text.</param>
/// <param name="DateOfBirth">The original date-of-birth cell text.</param>
/// <param name="Gender">The original gender cell text.</param>
/// <param name="JerseyNumber">The original jersey-number cell text.</param>
/// <param name="GraduationYear">The original graduation-year cell text.</param>
public sealed record PlayerImportRowValues(
    string FirstName,
    string LastName,
    string DateOfBirth,
    string Gender,
    string JerseyNumber,
    string GraduationYear);

/// <summary>Describes one field-specific validation failure.</summary>
/// <param name="Field">The stable field associated with the error.</param>
/// <param name="Message">The user-facing validation message.</param>
public sealed record PlayerImportFieldError(PlayerImportField Field, string Message);

/// <summary>Describes the record that caused an uploaded row to be classified as duplicate.</summary>
/// <param name="Kind">The kind of duplicate match.</param>
/// <param name="ExistingPlayerId">The existing player identifier for a persisted-player match.</param>
/// <param name="EarlierSourceRowNumber">The earlier source row for an upload-row match.</param>
public sealed record PlayerImportDuplicate(
    PlayerImportDuplicateKind Kind,
    long? ExistingPlayerId,
    int? EarlierSourceRowNumber);

/// <summary>Contains one bounded, stable row in a player import preview.</summary>
/// <param name="SourceRowNumber">The one-based logical spreadsheet row number.</param>
/// <param name="Values">The six original cell values.</param>
/// <param name="Candidate">The typed candidate when row validation succeeds.</param>
/// <param name="Status">The row validation and duplicate status.</param>
/// <param name="Errors">The bounded field validation errors.</param>
/// <param name="Duplicate">The duplicate match details when the row is a duplicate.</param>
public sealed record PlayerImportPreviewRow(
    int SourceRowNumber,
    PlayerImportRowValues Values,
    CreatePlayerInput? Candidate,
    PlayerImportRowStatus Status,
    IReadOnlyList<PlayerImportFieldError> Errors,
    PlayerImportDuplicate? Duplicate);

/// <summary>Contains the complete bounded result of validating one player CSV upload.</summary>
/// <param name="OperationId">The UUIDv7 identifier for this preview operation.</param>
/// <param name="ConfirmationToken">The opaque protected file-identity token.</param>
/// <param name="ExpiresAt">The server-issued token expiry timestamp.</param>
/// <param name="TotalRows">The total number of parsed data rows.</param>
/// <param name="ReadyRows">The number of ready rows.</param>
/// <param name="InvalidRows">The number of invalid rows.</param>
/// <param name="DuplicateRows">The number of duplicate rows.</param>
/// <param name="Rows">The ordered preview rows.</param>
public sealed record PlayerImportPreview(
    Guid OperationId,
    string ConfirmationToken,
    DateTimeOffset ExpiresAt,
    int TotalRows,
    int ReadyRows,
    int InvalidRows,
    int DuplicateRows,
    IReadOnlyList<PlayerImportPreviewRow> Rows);

/// <summary>Represents the generated player import template.</summary>
/// <param name="Content">The exact UTF-8 BOM-prefixed CSV bytes.</param>
/// <param name="ContentType">The response content type.</param>
/// <param name="DownloadFileName">The fixed safe download filename.</param>
public sealed record PlayerImportTemplate(byte[] Content, string ContentType, string DownloadFileName);
