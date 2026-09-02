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
public sealed record PlayerImportRowValues(
    string FirstName,
    string LastName,
    string DateOfBirth,
    string Gender,
    string JerseyNumber,
    string GraduationYear);

/// <summary>Describes one field-specific validation failure.</summary>
public sealed record PlayerImportFieldError(PlayerImportField Field, string Message);

/// <summary>Describes the record that caused an uploaded row to be classified as duplicate.</summary>
public sealed record PlayerImportDuplicate(
    PlayerImportDuplicateKind Kind,
    long? ExistingPlayerId,
    int? EarlierSourceRowNumber);

/// <summary>Contains one bounded, stable row in a player import preview.</summary>
public sealed record PlayerImportPreviewRow(
    int SourceRowNumber,
    PlayerImportRowValues Values,
    CreatePlayerInput? Candidate,
    PlayerImportRowStatus Status,
    IReadOnlyList<PlayerImportFieldError> Errors,
    PlayerImportDuplicate? Duplicate);

/// <summary>Contains the complete bounded result of validating one player CSV upload.</summary>
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
public sealed record PlayerImportTemplate(byte[] Content, string ContentType, string DownloadFileName);
