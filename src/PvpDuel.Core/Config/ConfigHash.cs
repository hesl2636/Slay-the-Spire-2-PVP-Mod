using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PvpDuel.Core.Config;

/// <summary>
/// Deterministic config fingerprint shared by both ends. Serialization is pinned
/// (sorted-ish canonical property order via explicit serializer options) so the
/// same logical config always hashes identically across processes and runs.
/// </summary>
public static class ConfigHash
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>Stable hash of a config (lowercase hex SHA-256 of the canonical JSON).</summary>
    public static string Compute(PvpConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Hex(ComputeBytes(config));
    }

    public static byte[] ComputeBytes(PvpConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        // Serialize via a canonical intermediate (ordered tuple) so future
        // property additions cannot silently reorder legacy hashes.
        string canonicalJson = JsonSerializer.Serialize(
            new object[] { config.PlayCap, config.DisconnectTimeoutSec, config.FatigueEnabled },
            CanonicalOptions);
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson));
    }

    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
