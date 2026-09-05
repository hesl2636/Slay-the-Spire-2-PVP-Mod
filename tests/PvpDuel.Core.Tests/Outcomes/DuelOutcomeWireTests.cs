using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

using PvpDuel.Core.Duel;
using PvpDuel.Logging;
using PvpDuel.Net;

using Xunit;

namespace PvpDuel.Core.Tests.Outcomes;

/// <summary>
/// Ticket #23: the codec and the actual <see cref="DuelOutcomeMessage"/> wire
/// format must agree — a result encoded into the message, serialized through
/// the game's PacketWriter/PacketReader and decoded again has to mirror the
/// original (acceptance #1: both ends' records must be identical). Runs
/// against the real game DLL; Logger stays off (headless test host).
/// </summary>
public class DuelOutcomeWireTests
{
    static DuelOutcomeWireTests() => PvpDuelLog.Enabled = false;

    private static DuelOutcomeMessage SerializeDeserialize(DuelOutcomeMessage message)
    {
        var writer = new PacketWriter();
        message.Serialize(writer);

        var reader = new PacketReader();
        reader.Reset(writer.Buffer[..writer.BytePosition]);
        var restored = new DuelOutcomeMessage();
        restored.Deserialize(reader);
        return restored;
    }

    [Theory]
    [InlineData(0, 1122334455667788, 2, (int)DuelEndReason.Kill)]
    [InlineData(2, 42, 77, (int)DuelEndReason.DoubleDeathFirstHandWins)]
    public void EncodedResult_SurvivesWireRoundTrip(int actIndex, ulong winner, ulong loser, int reason)
    {
        var result = new DuelResult(actIndex, winner, loser, (DuelEndReason)reason);

        var wire = DuelOutcomeCodec.Encode(result);
        var message = new DuelOutcomeMessage
        {
            ActIndex = wire.ActIndex,
            WinnerNetId = wire.WinnerNetId,
            LoserNetId = wire.LoserNetId,
            Reason = wire.Reason,
        };
        var roundTripped = SerializeDeserialize(message);

        var decoded = DuelOutcomeCodec.TryDecode(
            roundTripped.ActIndex, roundTripped.WinnerNetId, roundTripped.LoserNetId, roundTripped.Reason,
            out var restored);

        Assert.True(decoded);
        Assert.NotNull(restored);
        Assert.True(result.Mirrors(restored));
    }

    [Fact]
    public void WirePayload_WithUnknownReasonByte_IsUndecodable()
    {
        var roundTripped = SerializeDeserialize(new DuelOutcomeMessage
        {
            ActIndex = 1,
            WinnerNetId = 10,
            LoserNetId = 20,
            Reason = 200,
        });

        var decoded = DuelOutcomeCodec.TryDecode(
            roundTripped.ActIndex, roundTripped.WinnerNetId, roundTripped.LoserNetId, roundTripped.Reason,
            out var restored);

        Assert.False(decoded);
        Assert.Null(restored);
    }
}
