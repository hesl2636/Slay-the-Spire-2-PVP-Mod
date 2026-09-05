namespace PvpDuel.Core.Duel;

/// <summary>Why a duel ended. Both ends must mirror this value exactly.</summary>
public enum DuelEndReason
{
    /// <summary>Opponent creature died from damage.</summary>
    Kill,

    /// <summary>Both creatures died in the same turn; the first-hand player wins.</summary>
    DoubleDeathFirstHandWins,

    /// <summary>A player conceded.</summary>
    Forfeit,

    /// <summary>Opponent disconnected and exceeded the reconnect timeout.</summary>
    DisconnectTimeout,
}

/// <summary>
/// The mirrored outcome of one duel (both ends compute/confirm the same record).
/// NetIds are plain <see cref="ulong"/> values (Player.NetId) to keep Core
/// free of game types.
/// </summary>
public sealed record DuelResult(
    int ActIndex,
    ulong WinnerNetId,
    ulong LoserNetId,
    DuelEndReason Reason)
{
    /// <summary>Mirrors are equal when every field matches.</summary>
    public bool Mirrors(DuelResult other) =>
        other is not null
        && ActIndex == other.ActIndex
        && WinnerNetId == other.WinnerNetId
        && LoserNetId == other.LoserNetId
        && Reason == other.Reason;
}
