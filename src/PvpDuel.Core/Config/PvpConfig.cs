using System.Text.Json.Serialization;

namespace PvpDuel.Core.Config;

/// <summary>
/// Mod configuration. Both ends must agree byte-for-byte on the derived hash —
/// the handshake check (pre-duel popup) rejects a session earlier than the
/// native ModMismatch flow otherwise would.
/// </summary>
public sealed record PvpConfig
{
    /// <summary>Max card plays per turn during a duel (spec default 12).</summary>
    [JsonPropertyName("playCap")]
    public int PlayCap { get; init; } = 12;

    /// <summary>Seconds to wait for a disconnected opponent during a duel before a timeout loss.</summary>
    [JsonPropertyName("disconnectTimeoutSec")]
    public int DisconnectTimeoutSec { get; init; } = 120;

    /// <summary>Whether the fatigue (per-turn play cap) rule is enforced at all.</summary>
    [JsonPropertyName("fatigueEnabled")]
    public bool FatigueEnabled { get; init; } = true;

    public static PvpConfig Default { get; } = new();
}
