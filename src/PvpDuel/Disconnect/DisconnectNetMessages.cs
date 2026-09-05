using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace PvpDuel.Disconnect;

/// <summary>
/// Ticket #25 rejoin-state messages (spec §2.1(7) 重连恢复). Declaring the
/// types is sufficient — <c>MessageTypes.Initialize</c> auto-registers mod
/// <c>INetMessage</c> subtypes, no Harmony (same contract as Net.DuelMessages).
///
/// Why a pull (request → response) instead of a push on the host's
/// <c>RunLobby.PlayerRejoined</c>: the rejoined client rebuilds its
/// <c>NetService</c> and only registers handlers when a room materializes,
/// while the vanilla bus delivers buffered messages to handlers registered at
/// flush time (NetMessageBus.SetBufferMessages) — a host-pushed payload can
/// race the client's subscription and be lost. The client asks once per run
/// when it is live; the host answers with the current side-channel snapshot.
/// </summary>
internal static class DisconnectNetMessageDocs;

/// <summary>Client → host: "send me the current mod run state" (sent when the run is live and this run key has not been backfilled yet).</summary>
public sealed class DuelStateBackfillRequestMessage : INetMessage
{
    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Debug;

    public bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer)
    {
    }

    public void Deserialize(PacketReader reader)
    {
    }
}

/// <summary>
/// Host → rejoined peer: the full mod state snapshot (branch table, settled
/// duel results, Ancient picks) as the side-channel JSON (same schema and
/// codec as the on-disk side channel, ticket #21). The receiver validates the
/// run key and applies it through the same restore path a load would.
/// </summary>
public sealed class DuelStateBackfillMessage : INetMessage
{
    public string PayloadJson { get; set; } = string.Empty;

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Debug;

    public bool ShouldBuffer => false;

    public void Serialize(PacketWriter writer) => writer.WriteString(PayloadJson);

    public void Deserialize(PacketReader reader) => PayloadJson = reader.ReadString();
}
