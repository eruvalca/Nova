#pragma warning disable CA1819 // Binary transfer contracts expose buffers for JSON base64, multipart, and file responses.
namespace Nova.SharedKernel.Features.Players;

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

#pragma warning restore CA1819
