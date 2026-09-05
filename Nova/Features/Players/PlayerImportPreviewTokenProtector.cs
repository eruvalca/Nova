using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Nova.Shared.Features.Players;

namespace Nova.Features.Players;

/// <summary>Contains the server-only identity bound into a player import preview token.</summary>
/// <param name="Version">The confirmation schema version.</param>
/// <param name="OperationId">The server-issued logical operation.</param>
/// <param name="ClubId">The owning club.</param>
/// <param name="ActorUserId">The administrator who reviewed the file.</param>
/// <param name="FileSha256">The hexadecimal hash of the original bytes.</param>
/// <param name="FileLength">The exact file byte count.</param>
/// <param name="IssuedAt">The UTC issuance timestamp.</param>
/// <param name="ExpiresAt">The exclusive UTC authorization deadline.</param>
/// <param name="RowStatuses">Original eligibility for each ordered source data row.</param>
internal sealed record PlayerImportPreviewTokenPayload(
    int Version,
    Guid OperationId,
    long ClubId,
    long ActorUserId,
    string FileSha256,
    int FileLength,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<PlayerImportRowStatus> RowStatuses);

/// <summary>Protects and validates time-limited player-import preview identities.</summary>
/// <param name="dataProtectionProvider">The application's persisted data-protection keys.</param>
/// <param name="timeProvider">The clock for authoritative payload expiry.</param>
internal sealed class PlayerImportPreviewTokenProtector(
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
{
    /// <summary>Separates reviewed-row confirmations from previous or unrelated token formats.</summary>
    private const string Purpose = "Nova.PlayerImportPreview.v2";
    /// <summary>Protects both confirmation identity and its cryptographic expiration.</summary>
    private readonly ITimeLimitedDataProtector _protector = dataProtectionProvider
        .CreateProtector(Purpose)
        .ToTimeLimitedDataProtector();

    /// <summary>Protects the supplied preview identity for the requested lifetime.</summary>
    /// <param name="payload">The complete review identity.</param>
    /// <param name="lifetime">The duration measured from the payload's issuance time.</param>
    /// <returns>An opaque, authenticated confirmation token.</returns>
    public string Protect(PlayerImportPreviewTokenPayload payload, TimeSpan lifetime) =>
        _protector.Protect(JsonSerializer.Serialize(payload), payload.IssuedAt.Add(lifetime));

    /// <summary>Attempts to unprotect a non-expired preview identity.</summary>
    /// <param name="token">The untrusted opaque confirmation.</param>
    /// <param name="payload">The valid identity, or null on rejection.</param>
    /// <returns>Whether the version, shape, and timestamps are valid.</returns>
    public bool TryUnprotect(string token, out PlayerImportPreviewTokenPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > PlayerImportConstraints.MaxConfirmationTokenCharacters)
        {
            return false;
        }

        try
        {
            var json = _protector.Unprotect(token, out var protectedExpiresAt);
            var candidate = JsonSerializer.Deserialize<PlayerImportPreviewTokenPayload>(json);
            var now = timeProvider.GetUtcNow();
            if (candidate is not { Version: 2 }
                || candidate.IssuedAt > now
                || candidate.ExpiresAt <= now
                || candidate.ExpiresAt != protectedExpiresAt
                || candidate.ExpiresAt <= candidate.IssuedAt
                || candidate.ExpiresAt - candidate.IssuedAt != TimeSpan.FromMinutes(PlayerImportConstraints.PreviewLifetimeMinutes)
                || candidate.OperationId == Guid.Empty || candidate.OperationId.Version != 7
                || candidate.ClubId <= 0 || candidate.ActorUserId <= 0
                || candidate.FileLength is < 1 or > PlayerImportConstraints.MaxFileBytes
                || candidate.RowStatuses is null
                || candidate.RowStatuses.Count is < 1 or > PlayerImportConstraints.MaxDataRows
                || candidate.RowStatuses.Any(status => !Enum.IsDefined(status)))
            {
                return false;
            }

            payload = candidate;
            return true;
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
    /// <param name="token">The untrusted confirmation.</param>
    /// <param name="operationId">The submitted preview identity.</param>
    /// <param name="clubId">The independently authorized tenant.</param>
    /// <param name="actorUserId">The independently authorized actor.</param>
    /// <param name="content">The exact original file bytes.</param>
    /// <param name="payload">The validated review identity, or null on failure.</param>
    /// <returns>Whether every protected request binding matches.</returns>
    public bool TryValidate(
        string token,
        Guid operationId,
        long clubId,
        long actorUserId,
        ReadOnlySpan<byte> content,
        out PlayerImportPreviewTokenPayload? payload)
    {
        payload = null;
        if (!TryUnprotect(token, out var candidate) || candidate is null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(candidate.FileSha256))
        {
            return false;
        }

        byte[] protectedHash;
        try
        {
            protectedHash = Convert.FromHexString(candidate.FileSha256);
        }
        catch (FormatException)
        {
            return false;
        }

        if (protectedHash.Length != SHA256.HashSizeInBytes)
        {
            return false;
        }

        var expectedHash = SHA256.HashData(content);
        if (candidate.OperationId != operationId
            || candidate.ClubId != clubId
            || candidate.ActorUserId != actorUserId
            || candidate.FileLength != content.Length
            || !CryptographicOperations.FixedTimeEquals(protectedHash, expectedHash))
        {
            return false;
        }

        payload = candidate;
        return true;
    }
}
