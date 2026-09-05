using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

// v0.111.0 note: MessageTypes.Initialize already scans loaded mod assemblies
// for INetMessage subtypes (ReflectionHelper.GetSubtypesInMods<INetMessage>),
// so no Harmony registration is needed — declaring the types below is
// sufficient. Vanilla clients Warn-and-drop unknown message ids, which is
// harmless for anyone running without the mod.

namespace PvpDuel.Net;

/// <summary>
/// v0.111.0 note: <c>MessageTypes.Initialize</c> already scans loaded mod
/// assemblies for INetMessage subtypes
/// (<c>ReflectionHelper.GetSubtypesInMods&lt;INetMessage&gt;</c>), so no Harmony
/// registration is needed — declaring the types here is sufficient. Vanilla
/// clients silently Warn-and-drop unknown ids, which is harmless for anyone
/// running without the mod.
/// </summary>
internal static class DuelNetMessageDocs;

/// <summary>
/// Branch-state broadcast (bidirectional; host-authoritative). Payload carries
/// only serializable primitives — no RNG results, no model references.
/// STUB (ticket #13): wire format implemented, no sender/handler wired yet.
/// </summary>
public sealed class DuelBranchMessage : INetMessage
{
    public ulong PlayerNetId { get; set; }
    public int ActIndex { get; set; }
    public int Col { get; set; }
    public int Row { get; set; }
    public bool InBossWait { get; set; }

    /// <summary>Host sends are echoed to all clients.</summary>
    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.VeryDebug;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(PlayerNetId);
        writer.WriteInt(ActIndex);
        writer.WriteByte((byte)Col);
        writer.WriteByte((byte)Row);
        writer.WriteBool(InBossWait);
    }

    public void Deserialize(PacketReader reader)
    {
        PlayerNetId = reader.ReadULong();
        ActIndex = reader.ReadInt();
        Col = reader.ReadByte();
        Row = reader.ReadByte();
        InBossWait = reader.ReadBool();
    }
}

/// <summary>
/// Act timer broadcast (host → client): host-clock elapsed milliseconds for the
/// current act; the shorter time wins first hand. STUB (ticket #13).
/// </summary>
public sealed class DuelTimerMessage : INetMessage
{
    public int ActIndex { get; set; }
    public long ElapsedMs { get; set; }

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.VeryDebug;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(ActIndex);
        writer.WriteLong(ElapsedMs);
    }

    public void Deserialize(PacketReader reader)
    {
        ActIndex = reader.ReadInt();
        ElapsedMs = reader.ReadLong();
    }
}

/// <summary>
/// Duel outcome report (both ends send their local view; results settle only
/// when both sides agree — disagreement aborts the session per spec §8).
/// STUB (ticket #13).
/// </summary>
public sealed class DuelOutcomeMessage : INetMessage
{
    public int ActIndex { get; set; }
    public ulong WinnerNetId { get; set; }
    public ulong LoserNetId { get; set; }

    /// <summary>DuelEndReason as byte to stay serializer-primitive-only.</summary>
    public byte Reason { get; set; }

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.VeryDebug;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(ActIndex);
        writer.WriteULong(WinnerNetId);
        writer.WriteULong(LoserNetId);
        writer.WriteByte(Reason);
    }

    public void Deserialize(PacketReader reader)
    {
        ActIndex = reader.ReadInt();
        WinnerNetId = reader.ReadULong();
        LoserNetId = reader.ReadULong();
        Reason = reader.ReadByte();
    }
}

/// <summary>
/// Ancient body pick (winner → loser via host forward). Mutex validation lives
/// in the execution layer; this message only drives the loser's option mirroring.
/// STUB (ticket #13).
/// </summary>
public sealed class AncientPickMessage : INetMessage
{
    public int ActIndex { get; set; }

    /// <summary>ModelId entry of the picked Ancient body.</summary>
    public string AncientModelId { get; set; } = string.Empty;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.VeryDebug;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(ActIndex);
        writer.WriteString(AncientModelId);
    }

    public void Deserialize(PacketReader reader)
    {
        ActIndex = reader.ReadInt();
        AncientModelId = reader.ReadString();
    }
}
