namespace Nova.Shared.Features.Players;

/// <summary>Contains the raw bytes and browser-supplied metadata for a player CSV preview operation.</summary>
public sealed record PlayerImportUploadInput(byte[] Content, string FileName, string ContentType);
