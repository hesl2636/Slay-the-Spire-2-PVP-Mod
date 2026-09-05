using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using PvpDuel.Logging;
using PvpDuel.Net;

using Xunit;

namespace PvpDuel.Core.Tests;

/// <summary>
/// Ticket #20: the four duel messages must survive a full serialize→deserialize
/// round trip through the game's PacketWriter/PacketReader (wire contract §4.2:
/// payload is serializable primitives only). Runs against the real game DLL.
/// </summary>
public class DuelMessagesTests
{
    public DuelMessagesTests()
    {
        // Game Logger static ctor crashes the headless test host; the message
        // types only carry LogLevel metadata, never log on their own, but keep
        // the switch off to match the other game-touching suites.
        PvpDuelLog.Enabled = false;
    }

    private static byte[] Serialize(INetMessage message)
    {
        var writer = new PacketWriter();
        message.Serialize(writer);
        return writer.Buffer[..writer.BytePosition];
    }

    private static PacketReader ReaderFor(byte[] bytes)
    {
        var reader = new PacketReader();
        reader.Reset(bytes);
        return reader;
    }

    [Fact]
    public void DuelBranchMessage_RoundTrips()
    {
        DuelBranchMessage message = new()
        {
            PlayerNetId = 0x1122334455667788,
            ActIndex = 2,
            Col = 6,
            Row = 9,
            InBossWait = true,
        };

        var reader = ReaderFor(Serialize(message));
        DuelBranchMessage roundTripped = new();
        roundTripped.Deserialize(reader);

        Assert.Equal(message.PlayerNetId, roundTripped.PlayerNetId);
        Assert.Equal(message.ActIndex, roundTripped.ActIndex);
        Assert.Equal(message.Col, roundTripped.Col);
        Assert.Equal(message.Row, roundTripped.Row);
        Assert.Equal(message.InBossWait, roundTripped.InBossWait);
    }

    [Fact]
    public void DuelTimerMessage_RoundTrips()
    {
        DuelTimerMessage message = new()
        {
            ActIndex = 1,
            ElapsedMs = 1_234_567_890_123L,
        };

        var reader = ReaderFor(Serialize(message));
        DuelTimerMessage roundTripped = new();
        roundTripped.Deserialize(reader);

        Assert.Equal(message.ActIndex, roundTripped.ActIndex);
        Assert.Equal(message.ElapsedMs, roundTripped.ElapsedMs);
    }

    [Fact]
    public void DuelOutcomeMessage_RoundTrips()
    {
        DuelOutcomeMessage message = new()
        {
            ActIndex = 2,
            WinnerNetId = 42,
            LoserNetId = 77,
            Reason = 3,
        };

        var reader = ReaderFor(Serialize(message));
        DuelOutcomeMessage roundTripped = new();
        roundTripped.Deserialize(reader);

        Assert.Equal(message.ActIndex, roundTripped.ActIndex);
        Assert.Equal(message.WinnerNetId, roundTripped.WinnerNetId);
        Assert.Equal(message.LoserNetId, roundTripped.LoserNetId);
        Assert.Equal(message.Reason, roundTripped.Reason);
    }

    [Fact]
    public void AncientPickMessage_RoundTrips()
    {
        AncientPickMessage message = new()
        {
            ActIndex = 0,
            AncientModelId = "ANCIENT.PVP_TEST_BODY",
        };

        var reader = ReaderFor(Serialize(message));
        AncientPickMessage roundTripped = new();
        roundTripped.Deserialize(reader);

        Assert.Equal(message.ActIndex, roundTripped.ActIndex);
        Assert.Equal(message.AncientModelId, roundTripped.AncientModelId);
    }

    [Fact]
    public void DuelMessages_TransferContract_IsReliableBroadcast()
    {
        foreach (INetMessage message in new INetMessage[]
                 {
                     new DuelBranchMessage(),
                     new DuelTimerMessage(),
                     new DuelOutcomeMessage(),
                     new AncientPickMessage(),
                 })
        {
            Assert.True(message.ShouldBroadcast, message.GetType().Name);
            Assert.Equal(NetTransferMode.Reliable, message.Mode);
            Assert.True(message.ShouldBuffer, message.GetType().Name);
        }
    }
}
