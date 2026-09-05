using System.Diagnostics.CodeAnalysis;

namespace PvpDuel.Core.Duel;

/// <summary>
/// Wire boundary for <c>DuelOutcomeMessage</c> (ticket #23, spec §4.2): maps a
/// mirrored <see cref="DuelResult"/> to the message's primitive payload and
/// back. The reason byte is the enum's declaration order and part of the wire
/// contract — unknown bytes (mod-version skew, corruption) fail decoding so the
/// caller applies divergence semantics instead of guessing.
/// </summary>
public static class DuelOutcomeCodec
{
    /// <summary>Encodes a result into the message's primitive payload.</summary>
    public static (int ActIndex, ulong WinnerNetId, ulong LoserNetId, byte Reason) Encode(DuelResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return (result.ActIndex, result.WinnerNetId, result.LoserNetId, EncodeReason(result.Reason));
    }

    /// <summary>Maps a reason to its stable wire byte (declaration order).</summary>
    public static byte EncodeReason(DuelEndReason reason) => (byte)reason;

    /// <summary>
    /// Decodes the message payload back into a result. False when the reason
    /// byte is not a known <see cref="DuelEndReason"/> — <paramref name="result"/>
    /// is null then and the caller must treat the report as undecodable.
    /// </summary>
    public static bool TryDecode(int actIndex, ulong winnerNetId, ulong loserNetId, byte reason, [NotNullWhen(true)] out DuelResult? result)
    {
        if (!Enum.IsDefined(typeof(DuelEndReason), (DuelEndReason)reason))
        {
            result = null;
            return false;
        }

        result = new DuelResult(actIndex, winnerNetId, loserNetId, (DuelEndReason)reason);
        return true;
    }
}
