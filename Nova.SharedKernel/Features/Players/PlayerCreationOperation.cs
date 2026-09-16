using System.Globalization;

namespace Nova.SharedKernel.Features.Players;

/// <summary>Portable identity and lifetime rules for manual creation, including after expired receipts are removed.</summary>
public static class PlayerCreationOperation
{
    /// <summary>The fixed execution and recovery lifetime.</summary>
    public static TimeSpan Lifetime => TimeSpan.FromHours(24);

    /// <summary>Reads the UUIDv7 timestamp without accepting malformed or unrepresentable identities.</summary>
    public static bool TryGetCreatedAt(Guid operationId, out DateTimeOffset createdAt)
    {
        createdAt = default;
        var value = operationId.ToString("N", CultureInfo.InvariantCulture);
        if (value[12] != '7' || value[16] is not ('8' or '9' or 'a' or 'b')
            || !long.TryParse(value.AsSpan(0, 12), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var milliseconds)
            || milliseconds > DateTimeOffset.MaxValue.Subtract(Lifetime).ToUnixTimeMilliseconds())
        {
            return false;
        }

        createdAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        return true;
    }

    /// <summary>Accepts an operation within its exclusive 24-hour window, allowing one minute of clock skew.</summary>
    public static bool TryGetDeadline(Guid operationId, DateTimeOffset now, out DateTimeOffset deadline)
    {
        deadline = default;
        if (!TryGetCreatedAt(operationId, out var createdAt) || createdAt > now.AddMinutes(1))
        {
            return false;
        }

        deadline = createdAt.Add(Lifetime);
        return now < deadline;
    }
}
