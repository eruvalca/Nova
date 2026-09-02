namespace Nova.Shared.Features.Players;

/// <summary>Contains the raw bytes and browser-supplied metadata for a player CSV preview operation.</summary>
public sealed record PlayerImportUploadInput
{
    /// <summary>Gets the exact uploaded file bytes.</summary>
    public required byte[] Content { get; init; }

    /// <summary>Gets the browser-supplied source filename.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the browser-supplied declared content type.</summary>
    public required string ContentType { get; init; }
}
