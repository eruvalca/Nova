namespace Nova.Shared.Features.Players;

/// <summary>Contains the raw bytes and browser-supplied metadata for a player CSV preview operation.</summary>
/// <param name="Content">The exact uploaded file bytes.</param>
/// <param name="FileName">The browser-supplied source filename.</param>
/// <param name="ContentType">The browser-supplied declared content type.</param>
public sealed record PlayerImportUploadInput(byte[] Content, string FileName, string ContentType);
