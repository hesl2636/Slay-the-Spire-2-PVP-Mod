using PvpDuel.Core.Duel;

using Xunit;

namespace PvpDuel.Core.Tests.Outcomes;

/// <summary>
/// Ticket #23 (spec §4.2): the DuelOutcomeMessage payload is primitives only;
/// the codec is the boundary between those primitives and the mirrored
/// <see cref="DuelResult"/>. Unknown reason bytes (mod skew / corruption) must
/// fail decoding so the sync layer applies divergence semantics instead of
/// guessing.
/// </summary>
public class DuelOutcomeCodecTests
{
    [Theory]
    [InlineData(0, 111, 222, (int)DuelEndReason.Kill)]
    [InlineData(1, 4_294_967_296, 1, (int)DuelEndReason.DoubleDeathFirstHandWins)]
    [InlineData(2, 7, 9_223_372_036_854_775_80, (int)DuelEndReason.Forfeit)]
    [InlineData(2, 7, 9, (int)DuelEndReason.DisconnectTimeout)]
    public void EncodeDecode_RoundTrips(int actIndex, ulong winner, ulong loser, int reason)
    {
        var result = new DuelResult(actIndex, winner, loser, (DuelEndReason)reason);

        var wire = DuelOutcomeCodec.Encode(result);
        var decoded = DuelOutcomeCodec.TryDecode(wire.ActIndex, wire.WinnerNetId, wire.LoserNetId, wire.Reason, out var restored);

        Assert.True(decoded);
        Assert.NotNull(restored);
        Assert.True(result.Mirrors(restored));
    }

    [Theory]
    [InlineData((byte)4)]
    [InlineData((byte)99)]
    [InlineData((byte)255)]
    public void TryDecode_UnknownReasonByte_Fails(byte unknownReason)
    {
        var decoded = DuelOutcomeCodec.TryDecode(0, 10, 20, unknownReason, out var result);

        Assert.False(decoded);
        Assert.Null(result);
    }

    [Fact]
    public void EncodedReason_IsStableEnumOrder()
    {
        // Wire compatibility: the byte mapping is part of the message contract
        // (§4.2) and must not drift with enum reordering.
        Assert.Equal(0, DuelOutcomeCodec.EncodeReason(DuelEndReason.Kill));
        Assert.Equal(1, DuelOutcomeCodec.EncodeReason(DuelEndReason.DoubleDeathFirstHandWins));
        Assert.Equal(2, DuelOutcomeCodec.EncodeReason(DuelEndReason.Forfeit));
        Assert.Equal(3, DuelOutcomeCodec.EncodeReason(DuelEndReason.DisconnectTimeout));
    }

    [Fact]
    public void Encode_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => DuelOutcomeCodec.Encode(null!));
    }
}
