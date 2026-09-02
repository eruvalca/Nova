using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Nova.Features.Players;

/// <summary>Contains the server-only identity bound into a player import preview token.</summary>
internal sealed record PlayerImportPreviewTokenPayload(
    int Version,
    Guid OperationId,
    long ClubId,
    long ActorUserId,
    string FileSha256,
    int FileLength,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Protects and validates time-limited player-import preview identities.</summary>
internal sealed class PlayerImportPreviewTokenProtector(IDataProtectionProvider dataProtectionProvider)
{
    private const string Purpose = "Nova.PlayerImportPreview.v1";
    private readonly ITimeLimitedDataProtector _protector = dataProtectionProvider
        .CreateProtector(Purpose)
        .ToTimeLimitedDataProtector();

    /// <summary>Protects the supplied preview identity for the requested lifetime.</summary>
    public string Protect(PlayerImportPreviewTokenPayload payload, TimeSpan lifetime) =>
        _protector.Protect(JsonSerializer.Serialize(payload), lifetime);

    /// <summary>Attempts to unprotect a non-expired preview identity.</summary>
    public bool TryUnprotect(string token, out PlayerImportPreviewTokenPayload? payload)
    {
        payload = null;
        try
        {
            var json = _protector.Unprotect(token, out _);
            payload = JsonSerializer.Deserialize<PlayerImportPreviewTokenPayload>(json);
            return payload is { Version: 1 };
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Validates a token against the actor, tenant, operation, and exact uploaded bytes.</summary>
    public bool TryValidate(
        string token,
        Guid operationId,
        long clubId,
        long actorUserId,
        ReadOnlySpan<byte> content,
        out PlayerImportPreviewTokenPayload? payload)
    {
        if (!TryUnprotect(token, out payload) || payload is null)
        {
            return false;
        }

        var expectedHash = SHA256.HashData(content);
        return payload.OperationId == operationId
            && payload.ClubId == clubId
            && payload.ActorUserId == actorUserId
            && payload.FileLength == content.Length
            && CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(payload.FileSha256),
                expectedHash);
    }
}
